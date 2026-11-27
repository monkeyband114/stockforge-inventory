using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StockForge.Application.Billing;
using StockForge.Application.Warehouses;

namespace StockForge.Infrastructure.Pdf;

/// <summary>
/// Renders invoices with QuestPDF. QuestPDF's Community licence is free for organizations with annual revenue
/// under USD 1M; larger operators must buy a licence (see https://www.questpdf.com/license/).
/// </summary>
internal sealed class InvoicePdfRenderer : IInvoicePdfRenderer
{
    private const string Navy = "#0B1B3A";
    private const string Accent = "#1D4ED8";
    private const string Muted = "#64748B";
    private const string Border = "#E2E8F0";

    static InvoicePdfRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(InvoiceDocument document)
    {
        var org = document.Organization;
        var invoice = document.Invoice;
        var culture = CultureInfo.InvariantCulture;
        string Money(decimal value) => $"{invoice.Currency} {value.ToString("#,0.00", culture)}";

        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(9.5f).FontColor("#0F172A"));

            page.Header().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(org.Name).FontSize(18).SemiBold().FontColor(Navy);
                    foreach (var line in AddressLines(org.Address))
                    {
                        col.Item().Text(line).FontColor(Muted);
                    }

                    col.Item().Text(org.ContactEmail).FontColor(Muted);
                });
                row.ConstantItem(200).AlignRight().Column(col =>
                {
                    col.Item().AlignRight().Text("INVOICE").FontSize(20).Bold().FontColor(Accent);
                    col.Item().AlignRight().Text(invoice.Number).SemiBold();
                    col.Item().AlignRight().Text($"Issued {invoice.IssueDate:yyyy-MM-dd}").FontColor(Muted);
                    col.Item().AlignRight().Text($"Due {invoice.DueDate:yyyy-MM-dd}").FontColor(Muted);
                    if (invoice.OrderNumber is not null)
                    {
                        col.Item().AlignRight().Text($"Order {invoice.OrderNumber}").FontColor(Muted);
                    }
                });
            });

            page.Content().PaddingVertical(24).Column(col =>
            {
                col.Spacing(16);
                col.Item().Column(bill =>
                {
                    bill.Item().Text("BILL TO").FontSize(8).SemiBold().FontColor(Muted);
                    bill.Item().Text(invoice.BillingName).SemiBold();
                    foreach (var line in AddressLines(invoice.BillingAddress))
                    {
                        bill.Item().Text(line);
                    }

                    if (invoice.BillingTaxNumber is not null)
                    {
                        bill.Item().Text($"Tax ID: {invoice.BillingTaxNumber}").FontColor(Muted);
                    }
                });

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(22);
                        c.RelativeColumn(4);
                        c.RelativeColumn(1.2f);
                        c.RelativeColumn(1.6f);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(1.2f);
                        c.RelativeColumn(1.8f);
                    });

                    table.Header(h =>
                    {
                        foreach (var (label, right) in new[] { ("#", false), ("Item", false), ("Qty", true), ("Unit price", true), ("Discount", true), ("Tax", true), ("Amount", true) })
                        {
                            var cell = h.Cell().Background(Navy).PaddingVertical(6).PaddingHorizontal(4);
                            (right ? cell.AlignRight() : cell).Text(label).FontColor(Colors.White).SemiBold().FontSize(8.5f);
                        }
                    });

                    foreach (var line in invoice.Lines)
                    {
                        table.Cell().Element(Cell).Text(line.LineNumber.ToString(culture)).FontColor(Muted);
                        table.Cell().Element(Cell).Column(c =>
                        {
                            c.Item().Text(line.Description);
                            c.Item().Text(line.Sku).FontSize(8).FontColor(Muted);
                        });
                        table.Cell().Element(Cell).AlignRight().Text(line.Quantity.ToString("#,0.###", culture));
                        table.Cell().Element(Cell).AlignRight().Text(line.UnitPrice.ToString("#,0.00", culture));
                        table.Cell().Element(Cell).AlignRight().Text(line.DiscountAmount == 0 ? "—" : line.DiscountAmount.ToString("#,0.00", culture));
                        table.Cell().Element(Cell).AlignRight().Text($"{line.TaxRate * 100:0.##}%");
                        table.Cell().Element(Cell).AlignRight().Text(line.Total.ToString("#,0.00", culture));
                    }
                });

                col.Item().AlignRight().Width(240).Column(totals =>
                {
                    Total(totals, "Subtotal", Money(invoice.Subtotal));
                    Total(totals, "Discounts", "−" + Money(invoice.DiscountTotal));
                    Total(totals, "Tax", Money(invoice.TaxTotal));
                    totals.Item().PaddingTop(4).BorderTop(1).BorderColor(Navy).Row(r =>
                    {
                        r.RelativeItem().Text("Total").SemiBold();
                        r.RelativeItem().AlignRight().Text(Money(invoice.Total)).SemiBold();
                    });
                    if (invoice.AmountPaid > 0)
                    {
                        Total(totals, "Paid", "−" + Money(invoice.AmountPaid));
                    }

                    if (invoice.CreditedAmount > 0)
                    {
                        Total(totals, "Credits", "−" + Money(invoice.CreditedAmount));
                    }

                    totals.Item().PaddingTop(6).Background("#EFF6FF").Padding(6).Row(r =>
                    {
                        r.RelativeItem().Text("Balance due").SemiBold().FontColor(Accent);
                        r.RelativeItem().AlignRight().Text(Money(invoice.BalanceDue)).Bold().FontColor(Accent);
                    });
                });

                col.Item().Text($"Status: {invoice.Status}. Please quote {invoice.Number} with your payment.").FontColor(Muted);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                t.Span($"{org.Name} · generated by StockForge · page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        })).GeneratePdf();
    }

    private static IContainer Cell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Border).PaddingVertical(5).PaddingHorizontal(4);

    private static void Total(ColumnDescriptor column, string label, string value) =>
        column.Item().PaddingVertical(2).Row(r =>
        {
            r.RelativeItem().Text(label).FontColor(Muted);
            r.RelativeItem().AlignRight().Text(value);
        });

    private static IEnumerable<string> AddressLines(AddressDto? address)
    {
        if (address is null)
        {
            yield break;
        }

        yield return address.Line1;
        if (!string.IsNullOrWhiteSpace(address.Line2))
        {
            yield return address.Line2;
        }

        yield return string.Join(", ", new[] { address.City, address.State, address.PostalCode }.Where(p => !string.IsNullOrWhiteSpace(p)));
        yield return address.Country;
    }
}
