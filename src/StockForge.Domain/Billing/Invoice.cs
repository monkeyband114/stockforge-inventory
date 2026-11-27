using StockForge.Domain.Common;
using StockForge.Domain.Sales;

namespace StockForge.Domain.Billing;

public enum InvoiceStatus
{
    Draft = 1,
    Issued = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Overdue = 5,
    Cancelled = 6,
}

public enum PaymentMethod
{
    BankTransfer = 1,
    Card = 2,
    Cash = 3,
    Cheque = 4,
    Other = 5,
}

/// <summary>
/// Customer invoice. Amounts are snapshotted from the order at issue time. <see cref="BalanceDue"/> =
/// total − payments − credits; the status follows the balance and the due date.
/// </summary>
public sealed class Invoice : TenantEntity, IAuditable
{
    private readonly List<InvoiceLine> _lines = [];
    private readonly List<Payment> _payments = [];

    private Invoice() { }

    private Invoice(string number, SalesOrder order, Customer customer, DateOnly issueDate, int dueDays)
    {
        Number = Guard.Required(number, "Number", 32);
        SalesOrderId = order.Id;
        CustomerId = customer.Id;
        IssueDate = issueDate;
        DueDate = issueDate.AddDays(dueDays);
        Currency = order.Currency;
        Subtotal = order.Subtotal;
        DiscountTotal = order.DiscountTotal;
        TaxTotal = order.TaxTotal;
        Total = order.Total;
        BillingName = customer.Name;
        BillingEmail = customer.Email;
        BillingTaxNumber = customer.TaxNumber;
        BillingAddress = customer.BillingAddress?.Copy();
        foreach (var line in order.Lines.OrderBy(l => l.LineNumber))
        {
            _lines.Add(new InvoiceLine(Id, line));
        }

        Status = InvoiceStatus.Issued;
    }

    public string Number { get; private set; } = string.Empty;
    public Guid? SalesOrderId { get; private set; }
    public Guid CustomerId { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateOnly IssueDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string Currency { get; private set; } = "USD";
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal Total { get; private set; }
    public decimal AmountPaid { get; private set; }
    public decimal CreditedAmount { get; private set; }
    public string BillingName { get; private set; } = string.Empty;
    public string? BillingEmail { get; private set; }
    public string? BillingTaxNumber { get; private set; }
    public Address? BillingAddress { get; private set; }
    public Guid? PdfFileId { get; private set; }
    public DateTimeOffset? PdfGeneratedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public uint Version { get; private set; }
    public IReadOnlyCollection<InvoiceLine> Lines => _lines;
    public IReadOnlyCollection<Payment> Payments => _payments;

    public decimal BalanceDue => Total - AmountPaid - CreditedAmount;

    public static Invoice FromOrder(string number, SalesOrder order, Customer customer, DateOnly issueDate, int dueDays) =>
        new(number, order, customer, issueDate, dueDays);

    public Payment RecordPayment(decimal amount, PaymentMethod method, string? reference, DateTimeOffset paidAt, Guid recordedBy, DateOnly today)
    {
        EnsureOpen();
        Guard.Positive(amount, "Payment amount");
        if (amount > BalanceDue)
        {
            throw new DomainException("overpayment", $"The payment exceeds the balance due of {BalanceDue:0.00}.");
        }

        var payment = new Payment(Id, Money.Round(amount), method, reference, paidAt, recordedBy);
        _payments.Add(payment);
        AmountPaid += payment.Amount;
        Refresh(today);
        return payment;
    }

    /// <summary>Credits returned goods; never credits more than the invoice total.</summary>
    public decimal ApplyCredit(decimal amount, DateOnly today)
    {
        if (Status == InvoiceStatus.Cancelled)
        {
            return 0;
        }

        var credit = Math.Min(Money.Round(amount), Total - CreditedAmount);
        CreditedAmount += credit;
        Refresh(today);
        return credit;
    }

    public void Cancel(DateTimeOffset at)
    {
        EnsureOpen();
        if (AmountPaid > 0)
        {
            throw new DomainException("invoice_paid", "The invoice has payments recorded and cannot be cancelled.");
        }

        Status = InvoiceStatus.Cancelled;
        CancelledAt = at;
    }

    /// <summary>Re-evaluates the status from balance and due date (also used by the daily overdue job).</summary>
    public void Refresh(DateOnly today)
    {
        if (Status is InvoiceStatus.Cancelled or InvoiceStatus.Draft)
        {
            return;
        }

        Status = BalanceDue <= 0 ? InvoiceStatus.Paid
            : DueDate < today ? InvoiceStatus.Overdue
            : AmountPaid + CreditedAmount > 0 ? InvoiceStatus.PartiallyPaid
            : InvoiceStatus.Issued;
    }

    public void AttachPdf(Guid fileId, DateTimeOffset at)
    {
        PdfFileId = fileId;
        PdfGeneratedAt = at;
    }

    private void EnsureOpen()
    {
        if (Status is InvoiceStatus.Cancelled or InvoiceStatus.Paid)
        {
            throw new InvalidStateTransitionException("Invoice", Status.ToString(), "updated");
        }
    }
}

public sealed class InvoiceLine : TenantEntity
{
    private InvoiceLine() { }

    internal InvoiceLine(Guid invoiceId, SalesOrderLine line)
    {
        InvoiceId = invoiceId;
        LineNumber = line.LineNumber;
        ProductId = line.ProductId;
        Sku = line.Sku;
        Description = line.ProductName;
        Quantity = line.Quantity;
        UnitPrice = line.UnitPrice;
        DiscountAmount = line.DiscountAmount;
        TaxRate = line.TaxRate;
        TaxAmount = line.TaxAmount;
        Total = line.Total;
    }

    public Guid InvoiceId { get; private set; }
    public int LineNumber { get; private set; }
    public Guid? ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal TaxRate { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal Total { get; private set; }
}

public sealed class Payment : TenantEntity
{
    private Payment() { }

    internal Payment(Guid invoiceId, decimal amount, PaymentMethod method, string? reference, DateTimeOffset paidAt, Guid recordedBy)
    {
        InvoiceId = invoiceId;
        Amount = amount;
        Method = Enum.IsDefined(method) ? method : throw new DomainValidationException("Unknown payment method.");
        Reference = Guard.Optional(reference, "Reference", 100);
        PaidAt = paidAt;
        RecordedBy = recordedBy;
    }

    public Guid InvoiceId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Reference { get; private set; }
    public DateTimeOffset PaidAt { get; private set; }
    public Guid RecordedBy { get; private set; }
}
