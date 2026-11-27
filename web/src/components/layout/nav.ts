import {
  ArrowLeftRight,
  Boxes,
  Building2,
  ChartLine,
  ClipboardList,
  ClipboardCheck,
  Contact,
  Cog,
  Gauge,
  History,
  Landmark,
  ListChecks,
  Lock,
  Package,
  PackageOpen,
  Receipt,
  ServerCog,
  ShoppingCart,
  Truck,
  Wallet,
  Tags,
  Users,
  Warehouse,
  type LucideIcon,
} from "lucide-react";
import { P } from "@/lib/permissions";

export interface NavItem {
  label: string;
  href: string;
  icon: LucideIcon;
  /** Visible when the user has ANY of these permissions. */
  anyOf: string[];
  /** Exact-match highlighting (otherwise prefix match). */
  exact?: boolean;
}

export interface NavGroup {
  label: string;
  /** "tenant" groups are hidden when the session has no organization (platform super admins). */
  scope: "tenant" | "platform" | "any";
  items: NavItem[];
}

export const NAV: NavGroup[] = [
  {
    label: "Overview",
    scope: "tenant",
    items: [
      { label: "Dashboard", href: "/dashboard", icon: Gauge, anyOf: [P.reportsOrganization, P.inventoryAnalytics] },
      { label: "Warehouses", href: "/warehouses", icon: Warehouse, anyOf: [P.warehousesView] },
    ],
  },
  {
    label: "Inventory",
    scope: "tenant",
    items: [
      { label: "Stock levels", href: "/inventory", icon: Boxes, anyOf: [P.inventoryView], exact: true },
      { label: "Movements", href: "/inventory/movements", icon: History, anyOf: [P.inventoryView] },
      { label: "Adjustments", href: "/inventory/adjustments", icon: ClipboardCheck, anyOf: [P.inventoryView] },
      { label: "Transfers", href: "/inventory/transfers", icon: ArrowLeftRight, anyOf: [P.inventoryView] },
      { label: "Reservations", href: "/inventory/reservations", icon: Lock, anyOf: [P.inventoryView] },
    ],
  },
  {
    label: "Sales & fulfillment",
    scope: "tenant",
    items: [
      { label: "Orders", href: "/orders", icon: ShoppingCart, anyOf: [P.ordersView] },
      { label: "Fulfillment & receiving", href: "/fulfillment", icon: PackageOpen, anyOf: [P.ordersFulfill, P.purchasingReceive] },
      { label: "Customers", href: "/customers", icon: Contact, anyOf: [P.customersView] },
    ],
  },
  {
    label: "Procurement",
    scope: "tenant",
    items: [
      { label: "Purchase orders", href: "/purchase-orders", icon: ClipboardList, anyOf: [P.purchasingView], exact: true },
      { label: "Reorder suggestions", href: "/purchase-orders/suggestions", icon: Truck, anyOf: [P.purchasingCreate] },
      { label: "Suppliers", href: "/suppliers", icon: Building2, anyOf: [P.suppliersView] },
    ],
  },
  {
    label: "Finance",
    scope: "tenant",
    items: [
      { label: "Invoices", href: "/invoices", icon: Receipt, anyOf: [P.invoicesView] },
      { label: "Payables", href: "/payables", icon: Wallet, anyOf: [P.payablesView] },
      { label: "Finance overview", href: "/finance", icon: ChartLine, anyOf: [P.financeReports] },
    ],
  },
  {
    label: "Catalog",
    scope: "tenant",
    items: [
      { label: "Products", href: "/products", icon: Package, anyOf: [P.productsView], exact: true },
      { label: "Catalog settings", href: "/products/categories", icon: Tags, anyOf: [P.productsView] },
    ],
  },
  {
    label: "Administration",
    scope: "tenant",
    items: [
      { label: "Users & roles", href: "/admin/users", icon: Users, anyOf: [P.usersView] },
      { label: "Organization", href: "/admin/organization", icon: Building2, anyOf: [P.organizationView] },
      { label: "Audit log", href: "/admin/audit", icon: ListChecks, anyOf: [P.auditView] },
      { label: "Background jobs", href: "/admin/jobs", icon: ServerCog, anyOf: [P.jobsView] },
    ],
  },
  {
    label: "Platform",
    scope: "platform",
    items: [
      { label: "Organizations", href: "/platform", icon: Landmark, anyOf: [P.platformTenants] },
      { label: "Background jobs", href: "/admin/jobs", icon: Cog, anyOf: [P.platformJobs] },
    ],
  },
];

export function visibleNav(permissions: string[], hasTenant: boolean): NavGroup[] {
  const set = new Set(permissions);
  return NAV.filter((g) => (g.scope === "tenant" ? hasTenant : g.scope === "platform" ? !hasTenant : true))
    .map((g) => ({ ...g, items: g.items.filter((i) => i.anyOf.some((p) => set.has(p))) }))
    .filter((g) => g.items.length > 0);
}
