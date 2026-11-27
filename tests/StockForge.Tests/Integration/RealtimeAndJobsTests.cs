using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockForge.Application.Abstractions;
using StockForge.Application.Identity;
using StockForge.Domain.Platform;
using StockForge.Infrastructure;
using StockForge.Infrastructure.Persistence;
using StockForge.Tests.Integration.Support;

namespace StockForge.Tests.Integration;

public sealed class SignalRAuthorizationTests(ApiFixture fixture)
{
    [Fact]
    public async Task Connection_without_a_token_is_rejected()
    {
        await using var connection = Connect(token: null);
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
        Assert.Contains("401", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clients_only_receive_events_of_their_own_organization()
    {
        var a = await TestTenant.CreateAsync(fixture, "RealtimeA");
        var b = await TestTenant.CreateAsync(fixture, "RealtimeB");
        var product = await a.CreateProductAsync();

        var receivedByA = new ConcurrentQueue<JsonElement>();
        var receivedByB = new ConcurrentQueue<JsonElement>();
        await using var connectionA = Connect(a.Admin.AccessToken);
        await using var connectionB = Connect(b.Admin.AccessToken);
        connectionA.On<JsonElement>(RealtimeEvents.InventoryUpdated, e => receivedByA.Enqueue(e));
        connectionB.On<JsonElement>(RealtimeEvents.InventoryUpdated, e => receivedByB.Enqueue(e));
        await connectionA.StartAsync();
        await connectionB.StartAsync();
        Assert.Equal(a.TenantId.ToString(), await connectionA.InvokeAsync<string>("WhoAmI"));

        await a.StockInAsync(product.Id, 4);

        await WaitUntilAsync(() => !receivedByA.IsEmpty);
        var payload = receivedByA.First().GetProperty("payload");
        Assert.Equal(product.Id, payload.GetProperty("productId").GetGuid());
        Assert.Equal(4, payload.GetProperty("quantityOnHand").GetDecimal());
        await Task.Delay(300);
        Assert.Empty(receivedByB);
    }

    [Fact]
    public async Task Revoked_session_cannot_connect()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        using var anonymous = fixture.CreateClient();
        var login = (await anonymous.PostAsync<API.Controllers.AuthResponse>("/api/auth/login",
            new LoginRequest(tenant.AdminEmail, TestTenant.Password, null))).Ok();
        using var client = fixture.CreateClient(login.AccessToken);
        (await client.PostAsync<object>("/api/auth/logout")).Ok();

        await using var connection = Connect(login.AccessToken);
        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
    }

    private HubConnection Connect(string? token)
    {
        var server = fixture.Factory.Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, Infrastructure.Realtime.NotificationsHub.Path), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                if (token is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            })
            .Build();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(100);
        }

        Assert.True(condition(), "Timed out waiting for a realtime message.");
    }
}

public sealed class BackgroundJobTests(ApiFixture fixture)
{
    [Fact]
    public async Task Failing_job_is_retried_with_backoff_then_marked_failed_and_can_be_requeued()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        Guid jobId;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ScopeContext>().SetSystem(tenant.TenantId, "test");
            scope.ServiceProvider.GetRequiredService<IJobQueue>().Enqueue(new AlwaysFailingJob("boom"), new JobOptions { MaxAttempts = 3 });
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.SaveChangesAsync();
            jobId = await db.OutboxMessages.Where(m => m.TenantId == tenant.TenantId && m.Type == AlwaysFailingJob.JobType).Select(m => m.Id).SingleAsync();
        }

        // Parallel tests share the outbox, so another processor may hold the job for a moment.
        await fixture.ProcessOutboxUntilAsync(async () => (await LoadAsync(jobId)).Attempts >= 1);
        var afterFirst = await LoadAsync(jobId);
        Assert.Equal((OutboxStatus.Pending, 1), (afterFirst.Status, afterFirst.Attempts));
        Assert.True(afterFirst.NextAttemptAt > DateTimeOffset.UtcNow); // Backed off, not retried immediately.
        Assert.Contains("boom", afterFirst.LastError, StringComparison.Ordinal);

        // Jump past each back-off window.
        for (var i = 0; i < 2; i++)
        {
            using (AdjustableClock.Use(DateTimeOffset.UtcNow.AddHours(2 * (i + 1))))
            {
                await fixture.ProcessOutboxAsync();
            }
        }

        var failed = await LoadAsync(jobId);
        Assert.Equal((OutboxStatus.Failed, 3), (failed.Status, failed.Attempts));

        // Failed-job monitoring: the organization admin sees and re-queues it.
        var jobs = (await tenant.Admin.GetAsync<Application.Common.PagedResult<Application.Jobs.JobDto>>("/api/jobs?status=Failed")).Ok();
        Assert.Contains(jobs.Items, j => j.Id == jobId);
        (await tenant.Admin.PostAsync<object>($"/api/jobs/{jobId}/retry")).Ok();
        Assert.Equal(OutboxStatus.Pending, (await LoadAsync(jobId)).Status);
    }

    [Fact]
    public async Task Sensitive_payloads_are_encrypted_at_rest_and_wiped_after_delivery()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        using var anonymous = fixture.CreateClient();
        var correlationId = $"test-{Guid.NewGuid():N}";
        await anonymous.PostAsync<object>("/api/auth/forgot-password", new ForgotPasswordRequest(tenant.AdminEmail),
            new Dictionary<string, string> { ["X-Correlation-Id"] = correlationId });

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.CorrelationId == correlationId);
        Assert.True(message.IsPayloadProtected);
        Assert.DoesNotContain("reset-password", message.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(tenant.AdminEmail, message.Payload, StringComparison.Ordinal);

        await fixture.ProcessOutboxUntilAsync(async () =>
            (await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id)).Status == OutboxStatus.Succeeded);

        var delivered = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);
        Assert.Equal("{}", delivered.Payload);
        Assert.Contains("reset-password", fixture.Emails.LastTo(tenant.AdminEmail)!.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Low_stock_alert_job_emails_inventory_managers_of_the_same_tenant_only()
    {
        var tenant = await TestTenant.CreateAsync(fixture);
        var other = await TestTenant.CreateAsync(fixture);
        var (_, manager) = await tenant.CreateMemberAsync("InventoryManager");
        var (_, otherManager) = await other.CreateMemberAsync("InventoryManager");
        var product = await tenant.CreateProductAsync("Gloves", reorderLevel: 5);
        await tenant.StockInAsync(product.Id, 6);
        await tenant.Admin.PostAsync<object>("/api/inventory/stock-out",
            new Application.Inventory.StockOutRequest(tenant.MainWarehouseId, product.Id, 3, null, null, null));

        // Alert job → fans out one email job per recipient → delivered.
        await fixture.ProcessOutboxUntilAsync(() => Task.FromResult(fixture.Emails.LastTo(manager.User.Email) is not null));

        Assert.Contains("Low stock", fixture.Emails.LastTo(manager.User.Email)?.Subject ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Emails.Sent, m => m.To == otherManager.User.Email && m.Subject.Contains(product.Sku, StringComparison.Ordinal));
    }

    private async Task<OutboxMessage> LoadAsync(Guid id)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }
}
