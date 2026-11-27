using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using StockForge.Application.Abstractions;
using StockForge.Application.Security;
using StockForge.Domain.Common;
using StockForge.Domain.Inventory;
using StockForge.Infrastructure.Identity;
using StockForge.Infrastructure.Persistence;

namespace StockForge.Infrastructure.Realtime;

/// <summary>SignalR group names. Always derived server-side from validated claims, never from client input.</summary>
public static class RealtimeGroups
{
    /// <summary>Permissions that have their own topic group (kept small: each group costs a backplane subscription).</summary>
    public static readonly IReadOnlySet<string> PermissionTopics = new HashSet<string>(StringComparer.Ordinal)
    {
        Permissions.Inventory.ViewAllWarehouses,
        Permissions.Inventory.ApproveAdjustment,
        Permissions.Orders.Approve,
        Permissions.Orders.Fulfill,
        Permissions.Finance.ViewReports,
        Permissions.Jobs.View,
    };

    public static string Tenant(Guid tenantId) => $"t:{tenantId:N}";

    public static string Warehouse(Guid tenantId, Guid warehouseId) => $"t:{tenantId:N}:w:{warehouseId:N}";

    public static string Permission(Guid tenantId, string permission) => $"t:{tenantId:N}:p:{permission}";
}

/// <summary>
/// Real-time notifications hub. On connect, the server places the connection into the tenant group, its
/// accessible warehouse groups (or the "all warehouses" topic) and permission topics, based solely on the
/// validated membership snapshot. Clients cannot join groups themselves, so they can never receive another
/// organization's events.
/// </summary>
[Authorize]
public sealed class NotificationsHub(IMembershipSnapshotProvider snapshots, ILogger<NotificationsHub> logger) : Hub
{
    public const string Path = "/hubs/notifications";

    public override async Task OnConnectedAsync()
    {
        var principal = Context.User;
        var snapshot = principal is null ? null : await snapshots.GetForPrincipalAsync(principal, Context.ConnectionAborted);
        if (snapshot is null)
        {
            logger.LogWarning("Rejecting SignalR connection without a valid session");
            Context.Abort();
            return;
        }

        if (snapshot.TenantId is not { } tenantId)
        {
            await base.OnConnectedAsync();
            return;
        }

        var groups = new List<string> { RealtimeGroups.Tenant(tenantId) };
        if (snapshot.WarehouseIds is { } warehouseIds)
        {
            groups.AddRange(warehouseIds.Select(w => RealtimeGroups.Warehouse(tenantId, w)));
        }

        groups.AddRange(snapshot.Permissions
            .Where(RealtimeGroups.PermissionTopics.Contains)
            .Select(p => RealtimeGroups.Permission(tenantId, p)));

        foreach (var group in groups)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Lets the client confirm which organization it is subscribed to (diagnostics).</summary>
    public string? WhoAmI() => Context.User?.FindFirstValue(StockForgeClaims.TenantId);
}

internal sealed class SignalRRealtimeNotifier(IHubContext<NotificationsHub> hub, ILogger<SignalRRealtimeNotifier> logger) : IRealtimeNotifier
{
    public async Task PublishAsync(RealtimeMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = new { type = message.Event, tenantId = message.TenantId, payload = message.Payload, at = DateTimeOffset.UtcNow };
            var clients = message.Audience switch
            {
                RealtimeAudience.Warehouse when message.WarehouseId is { } warehouseId => hub.Clients.Groups(
                    RealtimeGroups.Warehouse(message.TenantId, warehouseId),
                    RealtimeGroups.Permission(message.TenantId, Permissions.Inventory.ViewAllWarehouses)),
                RealtimeAudience.Permission when message.Permission is { } permission =>
                    hub.Clients.Group(RealtimeGroups.Permission(message.TenantId, permission)),
                _ => hub.Clients.Group(RealtimeGroups.Tenant(message.TenantId)),
            };
            await clients.SendAsync(message.Event, envelope, cancellationToken);
        }
        catch (Exception ex)
        {
            // Real-time delivery is best effort; clients re-sync from the API on reconnect.
            logger.LogWarning(ex, "Failed to publish realtime event {Event}", message.Event);
        }
    }
}

/// <summary>Translates committed domain events into real-time notifications.</summary>
internal sealed class RealtimeDomainEventDispatcher(IRealtimeNotifier notifier, ITenantContext tenantContext) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        // Several changes to the same balance within one transaction collapse into the final state.
        var levels = events.OfType<InventoryLevelChangedEvent>()
            .GroupBy(e => (e.WarehouseId, e.ProductId))
            .Select(g => g.Last());

        foreach (var e in levels)
        {
            await notifier.PublishAsync(new RealtimeMessage(TenantOf(e), RealtimeEvents.InventoryUpdated,
                new { e.WarehouseId, e.ProductId, e.QuantityOnHand, e.QuantityReserved, e.QuantityAvailable },
                RealtimeAudience.Warehouse, e.WarehouseId), cancellationToken);
        }

        foreach (var e in events.OfType<StockMovementRecordedEvent>())
        {
            await notifier.PublishAsync(new RealtimeMessage(TenantOf(e), RealtimeEvents.WarehouseActivity,
                new { e.MovementId, e.WarehouseId, e.ProductId, Type = e.Type.ToString(), e.QuantityDelta, e.ReservedDelta },
                RealtimeAudience.Warehouse, e.WarehouseId), cancellationToken);
        }

        foreach (var e in events.OfType<StockTransferStatusChangedEvent>())
        {
            var payload = new { e.TransferId, e.Number, Status = e.Status.ToString(), e.SourceWarehouseId, e.DestinationWarehouseId };
            await notifier.PublishAsync(new RealtimeMessage(TenantOf(e), RealtimeEvents.TransferUpdated, payload,
                RealtimeAudience.Warehouse, e.SourceWarehouseId), cancellationToken);
            await notifier.PublishAsync(new RealtimeMessage(TenantOf(e), RealtimeEvents.TransferUpdated, payload,
                RealtimeAudience.Warehouse, e.DestinationWarehouseId), cancellationToken);
        }

        foreach (var e in events.OfType<StockAdjustmentStatusChangedEvent>())
        {
            var payload = new { e.AdjustmentId, e.Number, e.WarehouseId, e.ProductId, Status = e.Status.ToString(), e.QuantityChange };
            await notifier.PublishAsync(new RealtimeMessage(TenantOf(e), RealtimeEvents.AdjustmentUpdated, payload,
                RealtimeAudience.Warehouse, e.WarehouseId), cancellationToken);
            if (e.Status == AdjustmentStatus.PendingApproval)
            {
                await notifier.PublishAsync(new RealtimeMessage(TenantOf(e), RealtimeEvents.AdjustmentUpdated, payload,
                    RealtimeAudience.Permission, Permission: Permissions.Inventory.ApproveAdjustment), cancellationToken);
            }
        }
    }

    // Events raised by brand-new entities carry an empty tenant (stamped at save); fall back to the scope.
    private Guid TenantOf(IDomainEvent e) => e.TenantId != Guid.Empty ? e.TenantId : tenantContext.RequiredTenantId;
}

/// <summary>Collects published messages instead of sending them (tests, tools).</summary>
public sealed class RecordingRealtimeNotifier : IRealtimeNotifier
{
    private readonly List<RealtimeMessage> _messages = [];

    public IReadOnlyList<RealtimeMessage> Messages
    {
        get
        {
            lock (_messages)
            {
                return _messages.ToList();
            }
        }
    }

    public Task PublishAsync(RealtimeMessage message, CancellationToken cancellationToken)
    {
        lock (_messages)
        {
            _messages.Add(message);
        }

        return Task.CompletedTask;
    }
}
