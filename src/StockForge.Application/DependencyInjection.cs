using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockForge.Application.Abstractions;
using StockForge.Application.Audit;
using StockForge.Application.Billing;
using StockForge.Application.Catalog;
using StockForge.Application.Dashboard;
using StockForge.Application.Inventory;
using StockForge.Application.Jobs;
using StockForge.Application.Organization;
using StockForge.Application.Platform;
using StockForge.Application.Sales;
using StockForge.Application.Warehouses;

namespace StockForge.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // One ScopeContext per scope, exposed through both interfaces.
        services.AddScoped<ScopeContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ScopeContext>());
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<ScopeContext>());

        services.AddScoped<InventoryLedger>();
        services.AddScoped<InventoryService>();
        services.AddScoped<ReservationService>();
        services.AddScoped<StockAdjustmentService>();
        services.AddScoped<StockTransferService>();
        services.AddScoped<WarehouseService>();
        services.AddScoped<UnitService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<ProductService>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<AuditService>();
        services.AddScoped<JobMonitoringService>();
        services.AddScoped<PlatformService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<SalesOrderService>();
        services.AddScoped<InvoiceService>();
        return services;
    }
}
