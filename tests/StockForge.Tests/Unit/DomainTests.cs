using StockForge.Application.Catalog;
using StockForge.Application.Identity;
using StockForge.Application.Inventory;
using StockForge.Application.Security;
using StockForge.Domain.Catalog;
using StockForge.Domain.Common;
using StockForge.Domain.Inventory;

namespace StockForge.Tests.Unit;

public sealed class InventoryItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Receive_recalculates_weighted_average_cost()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());
        item.Receive(10, 2.00m, Now);
        item.Receive(30, 4.00m, Now);

        Assert.Equal(40, item.QuantityOnHand);
        Assert.Equal(3.5m, item.AverageUnitCost); // (10×2 + 30×4) / 40
    }

    [Fact]
    public void Reserve_beyond_available_throws_and_leaves_balance_untouched()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());
        item.Receive(10, 1, Now);
        item.Reserve(8, Now);

        var ex = Assert.Throws<InsufficientStockException>(() => item.Reserve(3, Now));
        Assert.Equal(2, ex.Available);
        Assert.Equal(8, item.QuantityReserved);
        Assert.Equal(2, item.QuantityAvailable);
    }

    [Fact]
    public void Issue_cannot_take_reserved_stock()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());
        item.Receive(10, 1, Now);
        item.Reserve(7, Now);

        Assert.Throws<InsufficientStockException>(() => item.Issue(4, Now));
        var change = item.Issue(3, Now);
        Assert.Equal(-3, change.OnHandDelta);
        Assert.Equal(7, item.QuantityOnHand);
    }

    [Fact]
    public void ConsumeReserved_reduces_on_hand_and_reserved_together()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());
        item.Receive(10, 1, Now);
        item.Reserve(6, Now);

        var change = item.ConsumeReserved(6, Now);

        Assert.Equal((-6m, -6m, 4m, 0m), (change.OnHandDelta, change.ReservedDelta, change.OnHandAfter, change.ReservedAfter));
        Assert.Throws<DomainException>(() => item.ConsumeReserved(1, Now));
    }

    [Fact]
    public void Negative_adjustment_cannot_drop_below_reserved()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());
        item.Receive(10, 1, Now);
        item.Reserve(8, Now);

        Assert.Throws<InsufficientStockException>(() => item.Adjust(-3, Now));
        item.Adjust(-2, Now);
        Assert.Equal(8, item.QuantityOnHand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Quantities_must_be_positive(decimal quantity)
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<DomainValidationException>(() => item.Receive(quantity, 1, Now));
        Assert.Throws<DomainValidationException>(() => item.Reserve(quantity, Now));
    }

    [Fact]
    public void Every_mutation_raises_a_level_changed_event()
    {
        var item = new InventoryItem(Guid.NewGuid(), Guid.NewGuid());
        item.Receive(5, 1, Now);
        item.Reserve(2, Now);

        var events = item.GetDomainEvents().OfType<InventoryLevelChangedEvent>().ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal(3, events[^1].QuantityAvailable);
    }
}

public sealed class StockDocumentTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Transfer_follows_request_dispatch_receive()
    {
        var transfer = new StockTransfer("TRF-1", Guid.NewGuid(), Guid.NewGuid(), [(Guid.NewGuid(), 5)], null, Guid.NewGuid(), Now);
        Assert.Throws<InvalidStateTransitionException>(() => transfer.MarkReceived(Guid.NewGuid(), Now));

        transfer.MarkDispatched(Guid.NewGuid(), Now);
        Assert.Throws<InvalidStateTransitionException>(() => transfer.Cancel(Guid.NewGuid(), Now));
        transfer.MarkReceived(Guid.NewGuid(), Now);

        Assert.Equal(TransferStatus.Completed, transfer.Status);
    }

    [Fact]
    public void Transfer_rejects_same_warehouse_and_duplicate_lines()
    {
        var warehouse = Guid.NewGuid();
        var product = Guid.NewGuid();
        Assert.Throws<DomainValidationException>(() =>
            new StockTransfer("TRF-1", warehouse, warehouse, [(product, 1)], null, Guid.NewGuid(), Now));
        Assert.Throws<DomainValidationException>(() =>
            new StockTransfer("TRF-1", warehouse, Guid.NewGuid(), [(product, 1), (product, 2)], null, Guid.NewGuid(), Now));
    }

    [Fact]
    public void Count_adjustment_records_variance_and_can_be_reviewed_once()
    {
        var adjustment = StockAdjustment.ForCount("ADJ-1", Guid.NewGuid(), Guid.NewGuid(), countedQuantity: 42, systemQuantity: 50,
            "aisle 3", Guid.NewGuid(), Now);
        Assert.Equal(-8, adjustment.QuantityChange);

        adjustment.Reject(Guid.NewGuid(), "Recount needed", Now);
        Assert.Throws<InvalidStateTransitionException>(() => adjustment.MarkApplied(Guid.NewGuid(), null, Now));
    }

    [Fact]
    public void Reservation_tracks_partial_consumption_and_release()
    {
        var reservation = new StockReservation(Guid.NewGuid(), Guid.NewGuid(), 10, MovementReference.Manual(), null, null);
        reservation.Consume(4, Now);
        Assert.Equal(6, reservation.RemainingQuantity);

        Assert.Equal(6, reservation.ReleaseRemaining(Now));
        Assert.Equal(ReservationStatus.Consumed, reservation.Status);
        Assert.Throws<InvalidStateTransitionException>(() => reservation.Consume(1, Now));
    }
}

public sealed class CatalogTests
{
    [Theory]
    [InlineData("400638133393", 1)]
    [InlineData("590123412345", 7)]
    public void Ean13_check_digit_matches_gs1(string body, int expected) =>
        Assert.Equal(expected, Barcodes.Ean13CheckDigit(body));

    [Fact]
    public void Generated_internal_barcodes_are_valid_ean13_in_store_range()
    {
        var code = Barcodes.CreateInternalEan13(12345);
        Assert.True(Barcodes.IsValidEan13(code));
        Assert.StartsWith("2", code);
    }

    [Fact]
    public void Invalid_ean13_is_rejected() =>
        Assert.Throws<DomainValidationException>(() => Barcodes.Normalize("4006381333932"));

    [Fact]
    public void Alternate_units_convert_to_base_quantity()
    {
        var baseUnit = Guid.NewGuid();
        var box = Guid.NewGuid();
        var product = new Product("SKU-1", "Paper", null, null, baseUnit, 3, 6, null, 10, 30);
        product.AddUnit(box, 5, null);

        Assert.Equal(15, product.ToBaseQuantity(3, box));
        Assert.Equal(3, product.ToBaseQuantity(3, null));
        Assert.Throws<DomainValidationException>(() => product.ToBaseQuantity(1, Guid.NewGuid()));
    }

    [Fact]
    public void Image_type_is_detected_from_magic_bytes_not_client_header()
    {
        Assert.Equal("image/png", ProductService.DetectImageType([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]));
        Assert.Equal("image/jpeg", ProductService.DetectImageType([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Null(ProductService.DetectImageType("<svg onload=alert(1)>"u8.ToArray()));
    }
}

public sealed class PermissionModelTests
{
    [Fact]
    public void Every_role_permission_is_a_known_permission() =>
        Assert.All(RolePermissionDefaults.Map.SelectMany(r => r.Value), p => Assert.True(Permissions.IsKnown(p), p));

    [Fact]
    public void Organization_roles_never_include_platform_permissions() =>
        Assert.All(RolePermissionDefaults.Map.Where(r => r.Key != RoleNames.SuperAdmin).SelectMany(r => r.Value),
            p => Assert.DoesNotContain("platform.", p, StringComparison.Ordinal));

    [Fact]
    public void Warehouse_staff_cannot_approve_purchase_orders_or_read_finance()
    {
        var staff = RolePermissionDefaults.Map[RoleNames.WarehouseStaff];
        Assert.DoesNotContain(Permissions.Procurement.ApprovePurchaseOrders, staff);
        Assert.DoesNotContain(Permissions.Finance.ViewReports, staff);
        Assert.DoesNotContain(Permissions.Inventory.ApproveAdjustment, staff);
        Assert.DoesNotContain(Permissions.Inventory.ViewAllWarehouses, staff);
        Assert.Contains(Permissions.Inventory.Count, staff);
    }

    [Fact]
    public void Inventory_manager_can_manage_products_transfer_and_approve()
    {
        var manager = RolePermissionDefaults.Map[RoleNames.InventoryManager];
        Assert.Contains(Permissions.Products.Manage, manager);
        Assert.Contains(Permissions.Inventory.Transfer, manager);
        Assert.Contains(Permissions.Inventory.ApproveAdjustment, manager);
        Assert.Contains(Permissions.Inventory.Analytics, manager);
    }
}

public sealed class ValidatorTests
{
    [Theory]
    [InlineData("short1A", false)]
    [InlineData("alllowercase1", false)]
    [InlineData("NoDigitsHere", false)]
    [InlineData("Valid-Passw0rd", true)]
    public void Registration_enforces_password_policy(string password, bool valid)
    {
        var result = new RegisterOrganizationRequestValidator().Validate(
            new RegisterOrganizationRequest("Org", "A", "B", "a@b.test", password, null, null));
        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Transfer_validator_rejects_duplicate_products_and_same_warehouse()
    {
        var warehouse = Guid.NewGuid();
        var product = Guid.NewGuid();
        var result = new CreateTransferRequestValidator().Validate(new CreateTransferRequest(warehouse, warehouse,
            [new TransferLineRequest(product, 1), new TransferLineRequest(product, 2)], null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateTransferRequest.DestinationWarehouseId));
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("only once", StringComparison.Ordinal));
    }

    [Fact]
    public void Member_validator_rejects_platform_permissions_and_unknown_roles()
    {
        var result = new UpdateMemberRequestValidator().Validate(new UpdateMemberRequest(null, ["SuperAdmin"], [],
            [Permissions.Platform.ManageTenants]));
        Assert.Equal(2, result.Errors.Count);
    }
}
