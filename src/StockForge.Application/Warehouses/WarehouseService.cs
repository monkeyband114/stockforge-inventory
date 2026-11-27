using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Domain.Common;
using StockForge.Domain.Inventory;

namespace StockForge.Application.Warehouses;

public sealed record AddressDto(string Line1, string? Line2, string City, string? State, string? PostalCode, string Country)
{
    public Address ToDomain() => new(Line1, Line2, City, State, PostalCode, Country);

    public static AddressDto? From(Address? address) => address is null
        ? null
        : new AddressDto(address.Line1, address.Line2, address.City, address.State, address.PostalCode, address.Country);
}

public sealed record WarehouseDto(
    Guid Id,
    string Code,
    string Name,
    WarehouseType Type,
    AddressDto? Address,
    string? Phone,
    string? Email,
    bool IsActive,
    bool CanOperate,
    DateTimeOffset CreatedAt,
    uint Version);

public sealed record CreateWarehouseRequest(string Code, string Name, WarehouseType Type, AddressDto? Address, string? Phone, string? Email);

public sealed record UpdateWarehouseRequest(string Name, WarehouseType Type, AddressDto? Address, string? Phone, string? Email, bool IsActive, uint? Version);

public sealed class AddressDtoValidator : AbstractValidator<AddressDto>
{
    public AddressDtoValidator()
    {
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Line2).MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.PostalCode).MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
    }
}

public sealed class CreateWarehouseRequestValidator : AbstractValidator<CreateWarehouseRequest>
{
    public CreateWarehouseRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Address!).SetValidator(new AddressDtoValidator()).When(x => x.Address is not null);
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrEmpty(x.Email));
    }
}

public sealed class UpdateWarehouseRequestValidator : AbstractValidator<UpdateWarehouseRequest>
{
    public UpdateWarehouseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Address!).SetValidator(new AddressDtoValidator()).When(x => x.Address is not null);
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrEmpty(x.Email));
    }
}

public sealed class WarehouseService(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<WarehouseDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var warehouses = await db.Warehouses.AsNoTracking()
            .Where(w => includeInactive || w.IsActive)
            .OrderBy(w => w.Code)
            .ToListAsync(cancellationToken);
        return warehouses.Select(ToDto).ToList();
    }

    public async Task<WarehouseDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToDto(await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
              ?? throw new NotFoundException("Warehouse", id));

    public async Task<WarehouseDto> CreateAsync(CreateWarehouseRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Warehouses.AnyAsync(w => w.Code == code, cancellationToken))
        {
            throw new ConflictException("duplicate_code", $"A warehouse with code '{code}' already exists.");
        }

        var warehouse = new Warehouse(code, request.Name, request.Type, request.Address?.ToDomain(), request.Phone, request.Email);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(warehouse);
    }

    public async Task<WarehouseDto> UpdateAsync(Guid id, UpdateWarehouseRequest request, CancellationToken cancellationToken)
    {
        var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
                        ?? throw new NotFoundException("Warehouse", id);
        EnsureVersion(warehouse.Version, request.Version);
        if (!request.IsActive && warehouse.IsActive &&
            await db.InventoryItems.AnyAsync(i => i.WarehouseId == id && i.QuantityOnHand > 0, cancellationToken))
        {
            throw new DomainException("warehouse_has_stock", "Move or write off all stock before deactivating this warehouse.");
        }

        warehouse.Update(request.Name, request.Type, request.Address?.ToDomain(), request.Phone, request.Email);
        warehouse.SetActive(request.IsActive);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(warehouse);
    }

    internal static void EnsureVersion(uint current, uint? expected)
    {
        if (expected is { } version && version != current)
        {
            throw new ConflictException("concurrency_conflict", "The record was modified by someone else. Reload and try again.");
        }
    }

    private WarehouseDto ToDto(Warehouse w) => new(
        w.Id, w.Code, w.Name, w.Type, AddressDto.From(w.Address), w.Phone, w.Email, w.IsActive,
        currentUser.CanAccessWarehouse(w.Id), w.CreatedAt, w.Version);
}
