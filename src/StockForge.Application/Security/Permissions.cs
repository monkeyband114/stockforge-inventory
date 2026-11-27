using System.Reflection;

namespace StockForge.Application.Security;

/// <summary>
/// Granular permission catalogue. Authorization policies check permissions, never role names; roles are only
/// named bundles of permissions (see <see cref="RolePermissionDefaults"/>).
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public static class Platform
    {
        public const string ManageTenants = "platform.tenants.manage";
        public const string ViewJobs = "platform.jobs.view";
    }

    public static class Organization
    {
        public const string View = "organization.view";
        public const string Manage = "organization.manage";
    }

    public static class Users
    {
        public const string View = "users.view";
        public const string Manage = "users.manage";
    }

    public static class Warehouses
    {
        public const string View = "warehouses.view";
        public const string Manage = "warehouses.manage";
    }

    public static class Products
    {
        public const string View = "products.view";
        public const string Manage = "products.manage";
    }

    public static class Inventory
    {
        /// <summary>View stock in the warehouses the member is assigned to.</summary>
        public const string View = "inventory.view";

        /// <summary>View stock in every warehouse, regardless of assignment.</summary>
        public const string ViewAllWarehouses = "inventory.view_all_warehouses";

        public const string Receive = "inventory.receive";
        public const string Issue = "inventory.issue";
        public const string Count = "inventory.count";
        public const string RequestAdjustment = "inventory.adjust.request";
        public const string ApproveAdjustment = "inventory.adjust.approve";
        public const string Transfer = "inventory.transfer";
        public const string Reserve = "inventory.reserve";
        public const string Analytics = "inventory.analytics";
        public const string Valuation = "inventory.valuation";
    }

    public static class Orders
    {
        public const string View = "orders.view";
        public const string Create = "orders.create";
        public const string Approve = "orders.approve";
        public const string Fulfill = "orders.fulfill";
        public const string Cancel = "orders.cancel";
        public const string ProcessReturns = "orders.returns";
    }

    public static class Customers
    {
        public const string View = "customers.view";
        public const string Manage = "customers.manage";
    }

    public static class Procurement
    {
        public const string ViewSuppliers = "suppliers.view";
        public const string ManageSuppliers = "suppliers.manage";
        public const string ViewPurchaseOrders = "purchasing.view";
        public const string CreatePurchaseOrders = "purchasing.create";
        public const string ApprovePurchaseOrders = "purchasing.approve";
        public const string ReceiveGoods = "purchasing.receive";
    }

    public static class Finance
    {
        public const string ViewInvoices = "invoices.view";
        public const string ManageInvoices = "invoices.manage";
        public const string RecordPayments = "payments.record";
        public const string ViewReports = "finance.reports";
    }

    public static class Reports
    {
        public const string Organization = "reports.organization";
    }

    public static class Audit
    {
        public const string View = "audit.view";
    }

    public static class Jobs
    {
        public const string View = "jobs.view";
        public const string Manage = "jobs.manage";
    }

    /// <summary>Every permission defined above, with its group, for discovery endpoints and validation.</summary>
    public static IReadOnlyList<PermissionDefinition> All { get; } = Discover();

    private static readonly HashSet<string> Known = All.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

    public static bool IsKnown(string permission) => Known.Contains(permission);

    private static List<PermissionDefinition> Discover() =>
        typeof(Permissions)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(group => group
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
                .Select(f => new PermissionDefinition((string)f.GetRawConstantValue()!, group.Name)))
            .OrderBy(p => p.Group).ThenBy(p => p.Name)
            .ToList();
}

public sealed record PermissionDefinition(string Name, string Group);
