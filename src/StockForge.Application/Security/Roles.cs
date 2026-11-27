namespace StockForge.Application.Security;

public static class RoleNames
{
    /// <summary>Platform-level role (not tenant scoped). Manages organizations.</summary>
    public const string SuperAdmin = "SuperAdmin";

    public const string OrganizationAdmin = "OrganizationAdmin";
    public const string InventoryManager = "InventoryManager";
    public const string WarehouseManager = "WarehouseManager";
    public const string WarehouseStaff = "WarehouseStaff";
    public const string ProcurementOfficer = "ProcurementOfficer";
    public const string SalesOfficer = "SalesOfficer";
    public const string FinanceManager = "FinanceManager";
    public const string Auditor = "Auditor";

    /// <summary>Roles that can be assigned to members of an organization.</summary>
    public static IReadOnlyList<string> TenantRoles { get; } =
    [
        OrganizationAdmin, InventoryManager, WarehouseManager, WarehouseStaff, ProcurementOfficer,
        SalesOfficer, FinanceManager, Auditor,
    ];

    public static bool IsTenantRole(string role) => TenantRoles.Contains(role, StringComparer.Ordinal);
}

/// <summary>
/// Default permission bundle of each role. Seeded into Identity role claims at startup; the snapshot used at
/// request time is read from the database, so operators can tune role definitions without redeploying.
/// </summary>
public static class RolePermissionDefaults
{
    private static readonly string[] Read =
    [
        Permissions.Organization.View, Permissions.Warehouses.View, Permissions.Products.View,
    ];

    public static IReadOnlyDictionary<string, string[]> Map { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [RoleNames.SuperAdmin] =
        [
            Permissions.Platform.ManageTenants, Permissions.Platform.ViewJobs,
        ],

        [RoleNames.OrganizationAdmin] = Permissions.All
            .Select(p => p.Name)
            .Where(p => !p.StartsWith("platform.", StringComparison.Ordinal))
            .ToArray(),

        [RoleNames.InventoryManager] =
        [
            .. Read,
            Permissions.Products.Manage,
            Permissions.Inventory.View, Permissions.Inventory.ViewAllWarehouses, Permissions.Inventory.Receive,
            Permissions.Inventory.Issue, Permissions.Inventory.Count, Permissions.Inventory.RequestAdjustment,
            Permissions.Inventory.ApproveAdjustment, Permissions.Inventory.Transfer, Permissions.Inventory.Reserve,
            Permissions.Inventory.Analytics, Permissions.Inventory.Valuation,
            Permissions.Orders.View, Permissions.Procurement.ViewSuppliers, Permissions.Procurement.ViewPurchaseOrders,
        ],

        [RoleNames.WarehouseManager] =
        [
            .. Read,
            Permissions.Inventory.View, Permissions.Inventory.Receive, Permissions.Inventory.Issue,
            Permissions.Inventory.Count, Permissions.Inventory.RequestAdjustment, Permissions.Inventory.Transfer,
            Permissions.Inventory.Reserve, Permissions.Inventory.Analytics,
            Permissions.Orders.View, Permissions.Orders.Fulfill, Permissions.Orders.ProcessReturns,
            Permissions.Procurement.ViewPurchaseOrders, Permissions.Procurement.ReceiveGoods,
        ],

        [RoleNames.WarehouseStaff] =
        [
            .. Read,
            Permissions.Inventory.View, Permissions.Inventory.Receive, Permissions.Inventory.Issue,
            Permissions.Inventory.Count, Permissions.Inventory.RequestAdjustment,
            Permissions.Orders.View, Permissions.Orders.Fulfill, Permissions.Procurement.ReceiveGoods,
        ],

        [RoleNames.ProcurementOfficer] =
        [
            .. Read,
            Permissions.Inventory.View, Permissions.Inventory.ViewAllWarehouses,
            Permissions.Procurement.ViewSuppliers, Permissions.Procurement.ManageSuppliers,
            Permissions.Procurement.ViewPurchaseOrders, Permissions.Procurement.CreatePurchaseOrders,
            Permissions.Finance.ViewPayables,
        ],

        [RoleNames.SalesOfficer] =
        [
            .. Read,
            Permissions.Inventory.View, Permissions.Inventory.ViewAllWarehouses, Permissions.Inventory.Reserve,
            Permissions.Orders.View, Permissions.Orders.Create, Permissions.Orders.Cancel,
            Permissions.Customers.View, Permissions.Customers.Manage, Permissions.Finance.ViewInvoices,
        ],

        [RoleNames.FinanceManager] =
        [
            .. Read,
            // Finance works across the whole organization: not restricted by warehouse assignments.
            Permissions.Inventory.ViewAllWarehouses, Permissions.Inventory.Valuation, Permissions.Orders.View, Permissions.Orders.Approve,
            Permissions.Customers.View, Permissions.Finance.ViewInvoices, Permissions.Finance.ManageInvoices,
            Permissions.Finance.RecordPayments, Permissions.Finance.ViewReports,
            Permissions.Procurement.ViewSuppliers, Permissions.Procurement.ViewPurchaseOrders,
            Permissions.Procurement.ApprovePurchaseOrders, Permissions.Reports.Organization,
            Permissions.Finance.ViewPayables, Permissions.Finance.ManagePayables,
        ],

        [RoleNames.Auditor] =
        [
            .. Read,
            Permissions.Users.View, Permissions.Inventory.View, Permissions.Inventory.ViewAllWarehouses,
            Permissions.Inventory.Analytics, Permissions.Inventory.Valuation, Permissions.Orders.View,
            Permissions.Customers.View, Permissions.Procurement.ViewSuppliers,
            Permissions.Procurement.ViewPurchaseOrders, Permissions.Finance.ViewInvoices,
            Permissions.Finance.ViewReports, Permissions.Reports.Organization, Permissions.Audit.View,
            Permissions.Finance.ViewPayables,
        ],
    };
}
