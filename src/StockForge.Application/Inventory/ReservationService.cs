using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Domain.Common;
using StockForge.Domain.Inventory;

namespace StockForge.Application.Inventory;

/// <summary>Manual stock holds. Reservations created by orders/transfers are managed by those documents.</summary>
public sealed class ReservationService(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITransactionRunner transactions,
    InventoryLedger ledger,
    TimeProvider clock)
{
    public async Task<PagedResult<StockReservationDto>> ListAsync(ReservationQuery query, CancellationToken cancellationToken)
    {
        var reservations = db.StockReservations.AsNoTracking();
        if (currentUser.AccessibleWarehouseIds is { } allowed)
        {
            var allowedIds = allowed.ToArray();
            reservations = reservations.Where(r => allowedIds.Contains(r.WarehouseId));
        }

        if (query.WarehouseId is { } warehouseId)
        {
            reservations = reservations.Where(r => r.WarehouseId == warehouseId);
        }

        if (query.ProductId is { } productId)
        {
            reservations = reservations.Where(r => r.ProductId == productId);
        }

        if (query.Status is { } status)
        {
            reservations = reservations.Where(r => r.Status == status);
        }

        return await Project(reservations).ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<StockReservationDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var dto = await Project(db.StockReservations.AsNoTracking().Where(r => r.Id == id))
            .FirstOrDefaultAsync(cancellationToken);
        return dto is not null && currentUser.CanAccessWarehouse(dto.WarehouseId)
            ? dto
            : throw new NotFoundException("Reservation", id);
    }

    public async Task<StockReservationDto> CreateAsync(CreateReservationRequest request, CancellationToken cancellationToken)
    {
        currentUser.EnsureWarehouseAccess(request.WarehouseId);
        var id = await transactions.ExecuteAsync(async token =>
        {
            var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == request.WarehouseId, token)
                            ?? throw new NotFoundException("Warehouse", request.WarehouseId);
            warehouse.EnsureActive();
            _ = await db.Products.AnyAsync(p => p.Id == request.ProductId && p.IsActive, token)
                ? true
                : throw new NotFoundException("Product", request.ProductId);

            var item = await ledger.LoadAsync(request.WarehouseId, request.ProductId, token);
            var reservation = new StockReservation(request.WarehouseId, request.ProductId, request.Quantity,
                new MovementReference(MovementReferenceType.StockReservation, null, request.ReferenceNumber),
                request.ExpiresAt, request.Notes);
            db.StockReservations.Add(reservation);
            ledger.Reserve(item, request.Quantity,
                new MovementReference(MovementReferenceType.StockReservation, reservation.Id, request.ReferenceNumber),
                request.Notes ?? "Manual reservation");
            await db.SaveChangesAsync(token);
            return reservation.Id;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<StockReservationDto> ReleaseAsync(Guid id, CancellationToken cancellationToken)
    {
        await transactions.ExecuteAsync(async token =>
        {
            var reservation = await db.StockReservations.FirstOrDefaultAsync(r => r.Id == id, token)
                              ?? throw new NotFoundException("Reservation", id);
            currentUser.EnsureWarehouseAccess(reservation.WarehouseId);
            if (reservation.ReferenceType != MovementReferenceType.StockReservation)
            {
                throw new DomainException("reservation_managed",
                    "This reservation belongs to another document and is released through it.");
            }

            await ReleaseAsync(reservation, "Reservation released", token);
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Releases manual reservations whose expiry passed. Runs from the worker per tenant.</summary>
    public async Task<int> ReleaseExpiredAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var expiredIds = await db.StockReservations
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt != null && r.ExpiresAt <= now &&
                        r.ReferenceType == MovementReferenceType.StockReservation)
            .OrderBy(r => r.ExpiresAt)
            .Select(r => r.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        foreach (var reservationId in expiredIds)
        {
            await transactions.ExecuteAsync(async token =>
            {
                var reservation = await db.StockReservations.FirstAsync(r => r.Id == reservationId, token);
                if (reservation.Status != ReservationStatus.Active)
                {
                    return false;
                }

                await ReleaseAsync(reservation, "Reservation expired", token);
                await db.SaveChangesAsync(token);
                return true;
            }, cancellationToken);
        }

        return expiredIds.Count;
    }

    private async Task ReleaseAsync(StockReservation reservation, string reason, CancellationToken token)
    {
        var item = await ledger.LoadAsync(reservation.WarehouseId, reservation.ProductId, token);
        var quantity = reservation.ReleaseRemaining(clock.GetUtcNow());
        if (quantity > 0)
        {
            ledger.Release(item, quantity,
                new MovementReference(reservation.ReferenceType, reservation.ReferenceId, reservation.ReferenceNumber), reason);
        }
    }

    private IQueryable<StockReservationDto> Project(IQueryable<StockReservation> reservations) =>
        from r in reservations
        join p in db.Products on r.ProductId equals p.Id
        join w in db.Warehouses on r.WarehouseId equals w.Id
        orderby r.CreatedAt descending, r.Id descending
        select new StockReservationDto(
            r.Id, r.WarehouseId, w.Code, r.ProductId, p.Sku, p.Name, r.Quantity, r.QuantityConsumed,
            r.QuantityReleased, r.Quantity - r.QuantityConsumed - r.QuantityReleased, r.Status, r.ReferenceType,
            r.ReferenceId, r.ReferenceNumber, r.ExpiresAt, r.Notes, r.CreatedAt);
}
