// Permission strings from src/StockForge.Application/Security/Permissions.cs.
export const P = {
  platformTenants: "platform.tenants.manage",
  platformJobs: "platform.jobs.view",
  organizationView: "organization.view",
  organizationManage: "organization.manage",
  usersView: "users.view",
  usersManage: "users.manage",
  warehousesView: "warehouses.view",
  warehousesManage: "warehouses.manage",
  productsView: "products.view",
  productsManage: "products.manage",
  inventoryView: "inventory.view",
  inventoryViewAll: "inventory.view_all_warehouses",
  inventoryReceive: "inventory.receive",
  inventoryIssue: "inventory.issue",
  inventoryCount: "inventory.count",
  inventoryAdjustRequest: "inventory.adjust.request",
  inventoryAdjustApprove: "inventory.adjust.approve",
  inventoryTransfer: "inventory.transfer",
  inventoryReserve: "inventory.reserve",
  inventoryAnalytics: "inventory.analytics",
  inventoryValuation: "inventory.valuation",
  reportsOrganization: "reports.organization",
  customersView: "customers.view",
  customersManage: "customers.manage",
  ordersView: "orders.view",
  ordersCreate: "orders.create",
  ordersApprove: "orders.approve",
  ordersFulfill: "orders.fulfill",
  ordersCancel: "orders.cancel",
  ordersReturns: "orders.returns",
  invoicesView: "invoices.view",
  invoicesManage: "invoices.manage",
  paymentsRecord: "payments.record",
  financeReports: "finance.reports",
  auditView: "audit.view",
  jobsView: "jobs.view",
  jobsManage: "jobs.manage",
} as const;

export type Permission = (typeof P)[keyof typeof P];

// Role names from src/StockForge.Application/Security/Roles.cs.
export const ROLE_LABELS: Record<string, string> = {
  SuperAdmin: "Super admin",
  OrganizationAdmin: "Organization admin",
  InventoryManager: "Inventory manager",
  WarehouseManager: "Warehouse manager",
  WarehouseStaff: "Warehouse staff",
  ProcurementOfficer: "Procurement officer",
  SalesOfficer: "Sales officer",
  FinanceManager: "Finance manager",
  Auditor: "Auditor",
};

export function roleLabel(role: string): string {
  return ROLE_LABELS[role] ?? role;
}
