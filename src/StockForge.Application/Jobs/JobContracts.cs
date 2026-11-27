using StockForge.Application.Abstractions;

namespace StockForge.Application.Jobs;

/// <summary>Delivers one email through the configured SMTP server.</summary>
public sealed record SendEmailJob(string To, string Subject, string HtmlBody, string? TextBody = null) : IJob
{
    public static string JobType => "email.send";
}

/// <summary>Raised when a product's available stock in a warehouse drops to or below its reorder level.</summary>
public sealed record LowStockAlertJob(Guid WarehouseId, Guid ProductId, decimal QuantityAvailable, decimal ReorderLevel) : IJob
{
    public static string JobType => "inventory.low_stock_alert";
}

/// <summary>Emails a per-organization inventory summary for the given day.</summary>
public sealed record DailyInventorySummaryJob(DateOnly Date) : IJob
{
    public static string JobType => "inventory.daily_summary";
}
