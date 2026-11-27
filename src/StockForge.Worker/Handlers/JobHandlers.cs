using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockForge.Application.Abstractions;
using StockForge.Application.Dashboard;
using StockForge.Application.Jobs;
using StockForge.Application.Security;
using StockForge.Infrastructure.Email;

namespace StockForge.Worker.Handlers;

public sealed class SendEmailJobHandler(IEmailSender sender) : IJobHandler<SendEmailJob>
{
    public Task HandleAsync(SendEmailJob job, CancellationToken cancellationToken) =>
        sender.SendAsync(new EmailMessage(job.To, job.Subject, job.HtmlBody, job.TextBody), cancellationToken);
}

/// <summary>Pushes a real-time low-stock alert and fans out one email job per recipient (each retried independently).</summary>
public sealed class LowStockAlertJobHandler(
    IAppDbContext db,
    ITenantContext tenant,
    IRealtimeNotifier notifier,
    IUserDirectory users,
    IJobQueue jobs,
    IOptions<AppUrlOptions> urls) : IJobHandler<LowStockAlertJob>
{
    public async Task HandleAsync(LowStockAlertJob job, CancellationToken cancellationToken)
    {
        var item = await db.InventoryItems.AsNoTracking()
            .Where(i => i.WarehouseId == job.WarehouseId && i.ProductId == job.ProductId)
            .Select(i => new
            {
                i.Product!.Name,
                i.Product.Sku,
                i.Product.ReorderLevel,
                WarehouseName = i.Warehouse!.Name,
                Available = i.QuantityOnHand - i.QuantityReserved,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (item is null || item.Available > item.ReorderLevel)
        {
            return; // Restocked before the alert was processed.
        }

        await notifier.PublishAsync(new RealtimeMessage(tenant.RequiredTenantId, RealtimeEvents.LowStock,
            new { job.WarehouseId, job.ProductId, item.Sku, item.Name, item.WarehouseName, QuantityAvailable = item.Available, item.ReorderLevel },
            RealtimeAudience.Warehouse, job.WarehouseId), cancellationToken);

        var recipients = await users.GetMemberEmailsAsync([RoleNames.InventoryManager, RoleNames.WarehouseManager], cancellationToken);
        var link = $"{urls.Value.FrontendBaseUrl.TrimEnd('/')}/inventory?lowStock=true";
        var (subject, html, text) = EmailTemplates.LowStock(item.Name, item.Sku, item.WarehouseName, item.Available, item.ReorderLevel, link);
        foreach (var email in recipients)
        {
            jobs.Enqueue(new SendEmailJob(email, subject, html, text));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Builds the organization's daily inventory summary and emails administrators and inventory managers.</summary>
public sealed class DailyInventorySummaryJobHandler(
    IAppDbContext db,
    ITenantContext tenant,
    DashboardService dashboards,
    IUserDirectory users,
    IJobQueue jobs,
    IOptions<AppUrlOptions> urls) : IJobHandler<DailyInventorySummaryJob>
{
    public async Task HandleAsync(DailyInventorySummaryJob job, CancellationToken cancellationToken)
    {
        var tenantId = tenant.RequiredTenantId;
        var organization = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
            .Select(t => new { t.Name, t.BaseCurrency }).FirstAsync(cancellationToken);
        var dashboard = await dashboards.GetOrganizationAsync(7, cancellationToken);
        var yesterday = dashboard.MovementTrend.FirstOrDefault(p => p.Date == job.Date.AddDays(-1));
        var culture = CultureInfo.InvariantCulture;

        var rows = new List<(string, string)>
        {
            ("Active products", dashboard.ActiveProducts.ToString(culture)),
            ("Active warehouses", dashboard.TotalWarehouses.ToString(culture)),
            ("Units on hand", dashboard.UnitsOnHand.ToString("#,0.###", culture)),
            ("Inventory value (cost)", $"{organization.BaseCurrency} {dashboard.InventoryCostValue.ToString("#,0.00", culture)}"),
            ("Low-stock items", dashboard.LowStockCount.ToString(culture)),
            ("Out-of-stock items", dashboard.OutOfStockCount.ToString(culture)),
            ("Adjustments awaiting approval", dashboard.PendingAdjustments.ToString(culture)),
            ("Open transfers", dashboard.OpenTransfers.ToString(culture)),
            ("Units received yesterday", (yesterday?.Inbound ?? 0).ToString("#,0.###", culture)),
            ("Units issued yesterday", (yesterday?.Outbound ?? 0).ToString("#,0.###", culture)),
        };

        var recipients = await users.GetMemberEmailsAsync([RoleNames.OrganizationAdmin, RoleNames.InventoryManager], cancellationToken);
        var (subject, html, text) = EmailTemplates.DailySummary(organization.Name, job.Date, rows, $"{urls.Value.FrontendBaseUrl.TrimEnd('/')}/dashboard");
        foreach (var email in recipients)
        {
            jobs.Enqueue(new SendEmailJob(email, subject, html, text));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
