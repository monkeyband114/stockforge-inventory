using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Application.Warehouses;
using StockForge.Domain.Billing;
using StockForge.Domain.Sales;

namespace StockForge.Application.Sales;

public sealed class CustomerService(IAppDbContext db, ISequenceGenerator sequences, ITransactionRunner transactions)
{
    private static readonly InvoiceStatus[] OpenStatuses = [InvoiceStatus.Issued, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue];

    public async Task<PagedResult<CustomerDto>> ListAsync(CustomerQuery query, CancellationToken cancellationToken)
    {
        var customers = db.Customers.AsNoTracking();
        if (query.IsActive is { } isActive)
        {
            customers = customers.Where(c => c.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            customers = customers.Where(c => c.Name.ToLower().Contains(term) || c.Code.ToLower().Contains(term) ||
                                             (c.Email != null && c.Email.ToLower().Contains(term)));
        }

        var page = await customers.OrderBy(c => c.Name)
            .Select(c => new
            {
                Customer = c,
                Balance = db.Invoices.Where(i => i.CustomerId == c.Id && OpenStatuses.Contains(i.Status))
                    .Sum(i => (decimal?)(i.Total - i.AmountPaid - i.CreditedAmount)) ?? 0,
                Orders = db.SalesOrders.Count(o => o.CustomerId == c.Id),
            })
            .ToPagedResultAsync(query, cancellationToken);

        return new PagedResult<CustomerDto>(page.Items.Select(x => ToDto(x.Customer, x.Balance, x.Orders)).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException("Customer", id);
        var balance = await db.Invoices.Where(i => i.CustomerId == id && OpenStatuses.Contains(i.Status))
            .SumAsync(i => (decimal?)(i.Total - i.AmountPaid - i.CreditedAmount), cancellationToken) ?? 0;
        var orders = await db.SalesOrders.CountAsync(o => o.CustomerId == id, cancellationToken);
        return ToDto(customer, balance, orders);
    }

    public async Task<CustomerDto> CreateAsync(CustomerRequest request, CancellationToken cancellationToken)
    {
        var id = await transactions.ExecuteAsync(async token =>
        {
            var code = $"CUS-{await sequences.NextValueAsync(SequenceNames.Customer, token):D5}";
            var customer = new Customer(code, request.Name, request.Email, request.Phone, request.TaxNumber,
                request.BillingAddress?.ToDomain(), request.ShippingAddress?.ToDomain(), request.PaymentTermsDays,
                request.CreditLimit, request.Notes);
            customer.SetActive(request.IsActive);
            db.Customers.Add(customer);
            await db.SaveChangesAsync(token);
            return customer.Id;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, CustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException("Customer", id);
        WarehouseService.EnsureVersion(customer.Version, request.Version);
        customer.Update(request.Name, request.Email, request.Phone, request.TaxNumber, request.BillingAddress?.ToDomain(),
            request.ShippingAddress?.ToDomain(), request.PaymentTermsDays, request.CreditLimit, request.Notes);
        customer.SetActive(request.IsActive);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private static CustomerDto ToDto(Customer c, decimal balance, int orders) => new(
        c.Id, c.Code, c.Name, c.Email, c.Phone, c.TaxNumber, AddressDto.From(c.BillingAddress), AddressDto.From(c.ShippingAddress),
        c.PaymentTermsDays, c.CreditLimit, c.Notes, c.IsActive, balance, orders, c.CreatedAt, c.Version);
}
