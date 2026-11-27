using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Billing;
using StockForge.Application.Organization;
using StockForge.Application.Sales;
using StockForge.Infrastructure.Email;

namespace StockForge.Worker.Handlers;

/// <summary>Renders the invoice PDF, stores it and (optionally) queues the customer email.</summary>
public sealed class GenerateInvoicePdfJobHandler(
    InvoiceService invoices,
    OrganizationService organization,
    IInvoicePdfRenderer renderer,
    IAppDbContext db,
    IFileStorage files,
    IJobQueue jobs,
    TimeProvider clock) : IJobHandler<GenerateInvoicePdfJob>
{
    public async Task HandleAsync(GenerateInvoicePdfJob job, CancellationToken cancellationToken)
    {
        var dto = await invoices.GetAsync(job.InvoiceId, cancellationToken);
        var pdf = renderer.Render(new InvoiceDocument(await organization.GetAsync(cancellationToken), dto));

        var invoice = await db.Invoices.FirstAsync(i => i.Id == job.InvoiceId, cancellationToken);
        if (invoice.PdfFileId is { } previous)
        {
            await files.DeleteAsync(previous, cancellationToken);
        }

        invoice.AttachPdf(files.Stage($"{dto.Number}.pdf", "application/pdf", pdf, "invoice-pdf"), clock.GetUtcNow());
        if (job.EmailCustomer && dto.BillingEmail is not null)
        {
            jobs.Enqueue(new InvoiceEmailJob(invoice.Id), new JobOptions { DeduplicationKey = $"invoice-email:{invoice.Id}" });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class InvoiceEmailJobHandler(InvoiceService invoices, OrganizationService organization, IEmailSender email) : IJobHandler<InvoiceEmailJob>
{
    public async Task HandleAsync(InvoiceEmailJob job, CancellationToken cancellationToken)
    {
        var invoice = await invoices.GetAsync(job.InvoiceId, cancellationToken);
        var pdf = await invoices.GetPdfAsync(job.InvoiceId, cancellationToken)
                  ?? throw new InvalidOperationException("The invoice PDF has not been generated yet.");
        if (invoice.BillingEmail is null)
        {
            return;
        }

        var org = await organization.GetAsync(cancellationToken);
        var total = $"{invoice.Currency} {invoice.Total.ToString("#,0.00", CultureInfo.InvariantCulture)}";
        var (subject, html, text) = EmailTemplates.InvoiceIssued(invoice.BillingName, org.Name, invoice.Number, total, invoice.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await email.SendAsync(new EmailMessage(invoice.BillingEmail, subject, html, text,
            [new EmailAttachment(pdf.FileName, pdf.ContentType, pdf.Content)]), cancellationToken);
    }
}

public sealed class OrderConfirmationEmailJobHandler(SalesOrderService orders, OrganizationService organization, IEmailSender email)
    : IJobHandler<OrderConfirmationEmailJob>
{
    public async Task HandleAsync(OrderConfirmationEmailJob job, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(job.OrderId, cancellationToken);
        if (order.CustomerEmail is null)
        {
            return;
        }

        var org = await organization.GetAsync(cancellationToken);
        var total = $"{order.Currency} {order.Total.ToString("#,0.00", CultureInfo.InvariantCulture)}";
        var (subject, html, text) = EmailTemplates.OrderConfirmation(order.CustomerName, org.Name, order.Number, total,
            order.Lines.Select(l => (l.ProductName, l.Quantity.ToString("0.###", CultureInfo.InvariantCulture))).ToList());
        await email.SendAsync(new EmailMessage(order.CustomerEmail, subject, html, text), cancellationToken);
    }
}

/// <summary>Notifies the customer by email and connected users in real time that a shipment left the warehouse.</summary>
public sealed class ShipmentNotificationJobHandler(
    SalesOrderService orders,
    ITenantContext tenant,
    IRealtimeNotifier realtime,
    IEmailSender email) : IJobHandler<ShipmentNotificationJob>
{
    public async Task HandleAsync(ShipmentNotificationJob job, CancellationToken cancellationToken)
    {
        var shipment = await orders.GetShipmentAsync(job.ShipmentId, cancellationToken);
        var order = await orders.GetAsync(shipment.SalesOrderId, cancellationToken);

        await realtime.PublishAsync(new RealtimeMessage(tenant.RequiredTenantId, RealtimeEvents.ShipmentUpdated,
            new { shipment.Id, shipment.Number, shipment.SalesOrderId, OrderNumber = order.Number, Status = shipment.Status.ToString(), shipment.TrackingNumber },
            RealtimeAudience.Warehouse, shipment.WarehouseId), cancellationToken);

        if (order.CustomerEmail is not null)
        {
            var items = string.Join(", ", shipment.Lines.Select(l => $"{l.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} × {l.Sku}"));
            var (subject, html, text) = EmailTemplates.ShipmentDispatched(order.CustomerName, order.Number, shipment.Carrier, shipment.TrackingNumber, items);
            await email.SendAsync(new EmailMessage(order.CustomerEmail, subject, html, text), cancellationToken);
        }
    }
}
