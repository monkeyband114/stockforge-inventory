using System.ComponentModel.DataAnnotations.Schema;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Application.Sales;
using StockForge.Application.Warehouses;
using StockForge.Domain.Billing;

namespace StockForge.Application.Billing;

public sealed record InvoiceSummaryDto(
    Guid Id,
    string Number,
    Guid? SalesOrderId,
    string? OrderNumber,
    Guid CustomerId,
    string CustomerName,
    InvoiceStatus Status,
    DateOnly IssueDate,
    DateOnly DueDate,
    string Currency,
    decimal Total,
    decimal AmountPaid,
    decimal BalanceDue,
    bool HasPdf);

public sealed record InvoiceLineDto(int LineNumber, string Sku, string Description, decimal Quantity, decimal UnitPrice, decimal DiscountAmount,
    decimal TaxRate, decimal TaxAmount, decimal Total);

public sealed record PaymentDto(Guid Id, decimal Amount, PaymentMethod Method, string? Reference, DateTimeOffset PaidAt, string? RecordedByName);

public sealed record InvoiceDto(
    Guid Id,
    string Number,
    Guid? SalesOrderId,
    string? OrderNumber,
    Guid CustomerId,
    InvoiceStatus Status,
    DateOnly IssueDate,
    DateOnly DueDate,
    string Currency,
    string BillingName,
    string? BillingEmail,
    string? BillingTaxNumber,
    AddressDto? BillingAddress,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal Total,
    decimal AmountPaid,
    decimal CreditedAmount,
    decimal BalanceDue,
    bool HasPdf,
    DateTimeOffset? PdfGeneratedAt,
    IReadOnlyList<InvoiceLineDto> Lines,
    IReadOnlyList<PaymentDto> Payments,
    uint Version);

public sealed record InvoiceQuery : PageQuery
{
    public InvoiceStatus? Status { get; init; }
    public Guid? CustomerId { get; init; }
    public bool OutstandingOnly { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record RecordPaymentRequest(decimal Amount, PaymentMethod Method, string? Reference, DateTimeOffset? PaidAt);

public sealed class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.PaidAt).Must(p => p <= clock.GetUtcNow().AddMinutes(5)).When(x => x.PaidAt.HasValue)
            .WithMessage("Payment date cannot be in the future.");
    }
}

public sealed record MonthlyFinancePoint(int Year, int Month, decimal Invoiced, decimal Collected);

public sealed record FinanceSummaryDto(
    string Currency,
    decimal InvoicedThisMonth,
    decimal CollectedThisMonth,
    decimal OutstandingTotal,
    decimal OverdueTotal,
    int OutstandingInvoices,
    int OverdueInvoices,
    decimal PaymentsLast30Days,
    IReadOnlyList<MonthlyFinancePoint> Monthly,
    IReadOnlyList<InvoiceSummaryDto> TopOverdue);

/// <summary>Raw SQL row for monthly invoiced/collected totals.</summary>
public sealed class MonthlyAmountRow
{
    [Column("year")]
    public int Year { get; set; }

    [Column("month")]
    public int Month { get; set; }

    [Column("amount")]
    public decimal Amount { get; set; }
}

/// <summary>Invoices, payments and the finance summary. Invoices are created by order approval, never by hand.</summary>
public sealed class InvoiceService(
    IAppDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ITransactionRunner transactions,
    IFileStorage files,
    IJobQueue jobs,
    IAuditTrail audit,
    IUserDirectory users,
    TimeProvider clock)
{
    private static readonly InvoiceStatus[] OpenStatuses = [InvoiceStatus.Issued, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue];

    public async Task<PagedResult<InvoiceSummaryDto>> ListAsync(InvoiceQuery query, CancellationToken cancellationToken)
    {
        var invoices = db.Invoices.AsNoTracking();
        if (query.Status is { } status)
        {
            invoices = invoices.Where(i => i.Status == status);
        }

        if (query.CustomerId is { } customerId)
        {
            invoices = invoices.Where(i => i.CustomerId == customerId);
        }

        if (query.OutstandingOnly)
        {
            invoices = invoices.Where(i => OpenStatuses.Contains(i.Status));
        }

        if (query.From is { } from)
        {
            invoices = invoices.Where(i => i.IssueDate >= from);
        }

        if (query.To is { } to)
        {
            invoices = invoices.Where(i => i.IssueDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            invoices = invoices.Where(i => i.Number.ToLower().Contains(term) || i.BillingName.ToLower().Contains(term));
        }

        return await Project(invoices.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Number))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<InvoiceDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).Include(i => i.Payments)
                          .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
                      ?? throw new NotFoundException("Invoice", id);
        var orderNumber = invoice.SalesOrderId is { } orderId
            ? await db.SalesOrders.Where(o => o.Id == orderId).Select(o => o.Number).FirstOrDefaultAsync(cancellationToken)
            : null;
        var names = await users.GetDisplayNamesAsync(invoice.Payments.Select(p => p.RecordedBy), cancellationToken);
        return new InvoiceDto(
            invoice.Id, invoice.Number, invoice.SalesOrderId, orderNumber, invoice.CustomerId, invoice.Status, invoice.IssueDate,
            invoice.DueDate, invoice.Currency, invoice.BillingName, invoice.BillingEmail, invoice.BillingTaxNumber,
            AddressDto.From(invoice.BillingAddress), invoice.Subtotal, invoice.DiscountTotal, invoice.TaxTotal, invoice.Total,
            invoice.AmountPaid, invoice.CreditedAmount, invoice.BalanceDue, invoice.PdfFileId.HasValue, invoice.PdfGeneratedAt,
            invoice.Lines.OrderBy(l => l.LineNumber).Select(l => new InvoiceLineDto(l.LineNumber, l.Sku, l.Description, l.Quantity,
                l.UnitPrice, l.DiscountAmount, l.TaxRate, l.TaxAmount, l.Total)).ToList(),
            invoice.Payments.OrderBy(p => p.PaidAt).Select(p => new PaymentDto(p.Id, p.Amount, p.Method, p.Reference, p.PaidAt,
                names.NameOf(p.RecordedBy))).ToList(),
            invoice.Version);
    }

    public async Task<InvoiceDto> RecordPaymentAsync(Guid id, RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        await transactions.ExecuteAsync(async token =>
        {
            var invoice = await db.Invoices.Include(i => i.Payments).FirstOrDefaultAsync(i => i.Id == id, token)
                          ?? throw new NotFoundException("Invoice", id);
            var payment = invoice.RecordPayment(request.Amount, request.Method, request.Reference, request.PaidAt ?? clock.GetUtcNow(),
                currentUser.RequiredUserId, await TodayAsync(token));
            db.Payments.Add(payment);
            audit.Record("PaymentRecorded", nameof(Invoice), invoice.Id.ToString(), new { invoice.Number, payment.Amount, payment.Method });
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<StoredFileData?> GetPdfAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.AsNoTracking().Where(i => i.Id == id).Select(i => new { i.PdfFileId }).FirstOrDefaultAsync(cancellationToken)
                      ?? throw new NotFoundException("Invoice", id);
        return invoice.PdfFileId is { } fileId ? await files.GetAsync(fileId, cancellationToken) : null;
    }

    public async Task RegeneratePdfAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.Invoices.AnyAsync(i => i.Id == id, cancellationToken))
        {
            throw new NotFoundException("Invoice", id);
        }

        jobs.Enqueue(new GenerateInvoicePdfJob(id, EmailCustomer: false));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Daily job: flags unpaid invoices past their due date as Overdue. Returns how many changed.</summary>
    public async Task<int> MarkOverdueAsync(CancellationToken cancellationToken)
    {
        var today = await TodayAsync(cancellationToken);
        var due = await db.Invoices
            .Where(i => (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid) && i.DueDate < today)
            .ToListAsync(cancellationToken);
        foreach (var invoice in due)
        {
            invoice.Refresh(today);
        }

        await db.SaveChangesAsync(cancellationToken);
        return due.Count;
    }

    public async Task<FinanceSummaryDto> GetSummaryAsync(int months, CancellationToken cancellationToken)
    {
        months = Math.Clamp(months, 3, 24);
        var tenantId = tenantContext.RequiredTenantId;
        var tenant = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => new { t.BaseCurrency, t.TimeZone })
            .FirstAsync(cancellationToken);
        var today = await TodayAsync(cancellationToken);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var firstMonth = monthStart.AddMonths(-(months - 1));
        var since = new DateTimeOffset(firstMonth.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var open = db.Invoices.AsNoTracking().Where(i => OpenStatuses.Contains(i.Status));
        var invoiced = await db.SqlQuery<MonthlyAmountRow>($"""
            SELECT EXTRACT(YEAR FROM issue_date)::int AS year, EXTRACT(MONTH FROM issue_date)::int AS month, COALESCE(SUM(total), 0) AS amount
            FROM invoices WHERE tenant_id = {tenantId} AND status <> 'Cancelled' AND issue_date >= {firstMonth}
            GROUP BY 1, 2
            """).ToListAsync(cancellationToken);
        var collected = await db.SqlQuery<MonthlyAmountRow>($"""
            SELECT EXTRACT(YEAR FROM paid_at AT TIME ZONE {tenant.TimeZone})::int AS year,
                   EXTRACT(MONTH FROM paid_at AT TIME ZONE {tenant.TimeZone})::int AS month, COALESCE(SUM(amount), 0) AS amount
            FROM payments WHERE tenant_id = {tenantId} AND paid_at >= {since}
            GROUP BY 1, 2
            """).ToListAsync(cancellationToken);

        var monthly = Enumerable.Range(0, months).Select(offset => firstMonth.AddMonths(offset)).Select(m => new MonthlyFinancePoint(
            m.Year, m.Month,
            invoiced.FirstOrDefault(r => r.Year == m.Year && r.Month == m.Month)?.Amount ?? 0,
            collected.FirstOrDefault(r => r.Year == m.Year && r.Month == m.Month)?.Amount ?? 0)).ToList();

        var last30 = clock.GetUtcNow().AddDays(-30);
        return new FinanceSummaryDto(
            tenant.BaseCurrency,
            monthly[^1].Invoiced,
            monthly[^1].Collected,
            await open.SumAsync(i => (decimal?)(i.Total - i.AmountPaid - i.CreditedAmount), cancellationToken) ?? 0,
            await open.Where(i => i.Status == InvoiceStatus.Overdue).SumAsync(i => (decimal?)(i.Total - i.AmountPaid - i.CreditedAmount), cancellationToken) ?? 0,
            await open.CountAsync(cancellationToken),
            await open.CountAsync(i => i.Status == InvoiceStatus.Overdue, cancellationToken),
            await db.Payments.AsNoTracking().Where(p => p.PaidAt >= last30).SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0,
            monthly,
            await Project(open.Where(i => i.Status == InvoiceStatus.Overdue).OrderBy(i => i.DueDate)).Take(10).ToListAsync(cancellationToken));
    }

    private IQueryable<InvoiceSummaryDto> Project(IQueryable<Invoice> invoices) =>
        from i in invoices
        join o in db.SalesOrders on i.SalesOrderId equals o.Id into orders
        from o in orders.DefaultIfEmpty()
        select new InvoiceSummaryDto(i.Id, i.Number, i.SalesOrderId, o != null ? o.Number : null, i.CustomerId, i.BillingName, i.Status,
            i.IssueDate, i.DueDate, i.Currency, i.Total, i.AmountPaid, i.Total - i.AmountPaid - i.CreditedAmount, i.PdfFileId != null);

    private async Task<DateOnly> TodayAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequiredTenantId;
        var timeZone = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.TimeZone).FirstAsync(cancellationToken);
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var z) ? z : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }
}
