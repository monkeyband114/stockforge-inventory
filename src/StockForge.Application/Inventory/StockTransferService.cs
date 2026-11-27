using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Domain.Common;
using StockForge.Domain.Inventory;

namespace StockForge.Application.Inventory;

/// <summary>
/// Inter-warehouse transfers. Request reserves stock at the source, dispatch consumes the reservation
/// (Transfer Out at the source's average cost), receive books it in at the destination (Transfer In at the
/// same cost), and cancel releases the reservation. Each step is one atomic transaction.
/// </summary>
public sealed class StockTransferService(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITransactionRunner transactions,
    InventoryLedger ledger,
    ISequenceGenerator sequences,
    IUserDirectory users,
    TimeProvider clock)
{
    public async Task<PagedResult<StockTransferDto>> ListAsync(TransferQuery query, CancellationToken cancellationToken)
    {
        var transfers = db.StockTransfers.AsNoTracking();
        if (currentUser.AccessibleWarehouseIds is { } allowed)
        {
            var allowedIds = allowed.ToArray();
            transfers = transfers.Where(t => allowedIds.Contains(t.SourceWarehouseId) || allowedIds.Contains(t.DestinationWarehouseId));
        }

        if (query.WarehouseId is { } warehouseId)
        {
            transfers = transfers.Where(t => t.SourceWarehouseId == warehouseId || t.DestinationWarehouseId == warehouseId);
        }

        if (query.Status is { } status)
        {
            transfers = transfers.Where(t => t.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToUpperInvariant();
            transfers = transfers.Where(t => t.Number.Contains(term));
        }

        var total = await transfers.LongCountAsync(cancellationToken);
        var ids = await transfers
            .OrderByDescending(t => t.RequestedAt)
            .Skip((query.SafePage - 1) * query.SafePageSize)
            .Take(query.SafePageSize)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        var items = await LoadDtosAsync(ids, cancellationToken);
        return new PagedResult<StockTransferDto>(items, query.SafePage, query.SafePageSize, total);
    }

    public async Task<StockTransferDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var dto = (await LoadDtosAsync([id], cancellationToken)).FirstOrDefault();
        if (dto is null || !(currentUser.CanAccessWarehouse(dto.Source.Id) || currentUser.CanAccessWarehouse(dto.Destination.Id)))
        {
            throw new NotFoundException("Stock transfer", id);
        }

        return dto;
    }

    public async Task<StockTransferDto> CreateAsync(CreateTransferRequest request, CancellationToken cancellationToken)
    {
        currentUser.EnsureWarehouseAccess(request.SourceWarehouseId);
        var userId = currentUser.RequiredUserId;
        var id = await transactions.ExecuteAsync(async token =>
        {
            var warehouses = await db.Warehouses
                .Where(w => w.Id == request.SourceWarehouseId || w.Id == request.DestinationWarehouseId)
                .ToListAsync(token);
            var source = warehouses.FirstOrDefault(w => w.Id == request.SourceWarehouseId)
                         ?? throw new NotFoundException("Warehouse", request.SourceWarehouseId);
            var destination = warehouses.FirstOrDefault(w => w.Id == request.DestinationWarehouseId)
                              ?? throw new NotFoundException("Warehouse", request.DestinationWarehouseId);
            source.EnsureActive();
            destination.EnsureActive();

            var productIds = request.Lines.Select(l => l.ProductId).ToList();
            var foundProducts = await db.Products.CountAsync(p => productIds.Contains(p.Id) && p.IsActive, token);
            if (foundProducts != productIds.Count)
            {
                throw new NotFoundException("Product");
            }

            var number = $"TRF-{await sequences.NextValueAsync(SequenceNames.StockTransfer, token):D6}";
            var transfer = new StockTransfer(number, source.Id, destination.Id,
                request.Lines.Select(l => (l.ProductId, l.Quantity)).ToList(), request.Notes, userId, clock.GetUtcNow());
            db.StockTransfers.Add(transfer);

            var items = await ledger.LoadAsync(productIds.Select(p => (source.Id, p)).ToList(), token);
            var reference = new MovementReference(MovementReferenceType.StockTransfer, transfer.Id, number);
            foreach (var line in transfer.Lines.OrderBy(l => l.ProductId))
            {
                db.StockReservations.Add(new StockReservation(source.Id, line.ProductId, line.Quantity, reference, null,
                    $"Transfer {number} to {destination.Code}"));
                ledger.Reserve(items[(source.Id, line.ProductId)], line.Quantity, reference, $"Reserved for transfer to {destination.Code}");
            }

            await db.SaveChangesAsync(token);
            return transfer.Id;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<StockTransferDto> DispatchAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId;
        await transactions.ExecuteAsync(async token =>
        {
            var transfer = await LoadAsync(id, token);
            currentUser.EnsureWarehouseAccess(transfer.SourceWarehouseId);
            transfer.MarkDispatched(userId, clock.GetUtcNow());

            var reservations = await LoadReservationsAsync(transfer.Id, token);
            var items = await ledger.LoadAsync(transfer.Lines.Select(l => (transfer.SourceWarehouseId, l.ProductId)).ToList(), token);
            var reference = new MovementReference(MovementReferenceType.StockTransfer, transfer.Id, transfer.Number);
            var now = clock.GetUtcNow();
            foreach (var line in transfer.Lines.OrderBy(l => l.ProductId))
            {
                var item = items[(transfer.SourceWarehouseId, line.ProductId)];
                line.SetDispatchCost(item.AverageUnitCost);
                ledger.ConsumeReserved(item, line.Quantity, StockMovementType.TransferOut, reference, "Dispatched");
                reservations[line.ProductId].Consume(line.Quantity, now);
            }

            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<StockTransferDto> ReceiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId;
        await transactions.ExecuteAsync(async token =>
        {
            var transfer = await LoadAsync(id, token);
            currentUser.EnsureWarehouseAccess(transfer.DestinationWarehouseId);
            var destination = await db.Warehouses.FirstAsync(w => w.Id == transfer.DestinationWarehouseId, token);
            destination.EnsureActive();
            transfer.MarkReceived(userId, clock.GetUtcNow());

            var items = await ledger.LoadAsync(transfer.Lines.Select(l => (transfer.DestinationWarehouseId, l.ProductId)).ToList(), token);
            var reference = new MovementReference(MovementReferenceType.StockTransfer, transfer.Id, transfer.Number);
            foreach (var line in transfer.Lines.OrderBy(l => l.ProductId))
            {
                ledger.Receive(items[(transfer.DestinationWarehouseId, line.ProductId)], line.Quantity, line.UnitCost,
                    StockMovementType.TransferIn, reference, "Received");
            }

            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<StockTransferDto> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId;
        await transactions.ExecuteAsync(async token =>
        {
            var transfer = await LoadAsync(id, token);
            currentUser.EnsureWarehouseAccess(transfer.SourceWarehouseId);
            transfer.Cancel(userId, clock.GetUtcNow());

            var reservations = await LoadReservationsAsync(transfer.Id, token);
            var items = await ledger.LoadAsync(transfer.Lines.Select(l => (transfer.SourceWarehouseId, l.ProductId)).ToList(), token);
            var reference = new MovementReference(MovementReferenceType.StockTransfer, transfer.Id, transfer.Number);
            var now = clock.GetUtcNow();
            foreach (var line in transfer.Lines.OrderBy(l => l.ProductId))
            {
                var released = reservations[line.ProductId].ReleaseRemaining(now);
                if (released > 0)
                {
                    ledger.Release(items[(transfer.SourceWarehouseId, line.ProductId)], released, reference, "Transfer cancelled");
                }
            }

            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<StockTransfer> LoadAsync(Guid id, CancellationToken token) =>
        await db.StockTransfers.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == id, token)
        ?? throw new NotFoundException("Stock transfer", id);

    private async Task<Dictionary<Guid, StockReservation>> LoadReservationsAsync(Guid transferId, CancellationToken token)
    {
        var reservations = await db.StockReservations
            .Where(r => r.ReferenceType == MovementReferenceType.StockTransfer && r.ReferenceId == transferId &&
                        r.Status == ReservationStatus.Active)
            .ToListAsync(token);
        return reservations.ToDictionary(r => r.ProductId);
    }

    private async Task<IReadOnlyList<StockTransferDto>> LoadDtosAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var transfers = await db.StockTransfers.AsNoTracking().Include(t => t.Lines)
            .Where(t => ids.Contains(t.Id))
            .ToListAsync(cancellationToken);
        var warehouseIds = transfers.SelectMany(t => new[] { t.SourceWarehouseId, t.DestinationWarehouseId }).Distinct().ToList();
        var productIds = transfers.SelectMany(t => t.Lines.Select(l => l.ProductId)).Distinct().ToList();
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => new WarehouseRefDto(w.Id, w.Code, w.Name), cancellationToken);
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.Name })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var names = await users.GetDisplayNamesAsync(transfers.Select(t => t.RequestedBy).Distinct(), cancellationToken);

        return ids
            .Select(id => transfers.FirstOrDefault(t => t.Id == id))
            .Where(t => t is not null)
            .Select(t => new StockTransferDto(
                t!.Id,
                t.Number,
                warehouses[t.SourceWarehouseId],
                warehouses[t.DestinationWarehouseId],
                t.Status,
                t.Notes,
                t.RequestedBy,
                names.NameOf(t.RequestedBy),
                t.RequestedAt,
                t.DispatchedAt,
                t.ReceivedAt,
                t.CancelledAt,
                t.Lines.Sum(l => l.Quantity),
                t.Lines
                    .Select(l => new StockTransferLineDto(l.ProductId, products[l.ProductId].Sku, products[l.ProductId].Name, l.Quantity, l.UnitCost))
                    .OrderBy(l => l.Sku)
                    .ToList()))
            .ToList();
    }
}
