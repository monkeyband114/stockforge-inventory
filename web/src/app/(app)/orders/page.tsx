"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ChevronRight, Download, Plus, ShoppingCart } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Badge } from "@/components/ui/Badge";
import { Button, ButtonLink } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { controlClasses } from "@/components/ui/Field";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { PageSpinner } from "@/components/ui/Spinner";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { FilterSelect, NoResults, QueryBoundary, SearchInput } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { useListParams } from "@/features/common/useListParams";
import { CustomerPicker, type PickedCustomer } from "@/features/sales/CustomerPicker";
import { ORDER_STATUS_LABELS, OrderStatusBadge } from "@/features/sales/SalesBadges";
import { api, fetchAllPages } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { downloadCsv, timestampedName, toCsv } from "@/lib/csv";
import { formatCurrency, formatDate, formatQty } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useAccessibleWarehouses } from "@/lib/queries";
import { SALES_ORDER_STATUSES, type OrderSummary, type PagedResult, type SalesOrderStatus } from "@/lib/types";

// Quick tabs for the most common statuses; the status select covers the rest (the API filters one status).
const QUICK_TABS: { value: SalesOrderStatus | ""; label: string }[] = [
  { value: "", label: "All" },
  { value: "Draft", label: "Drafts" },
  { value: "PendingApproval", label: "Pending approval" },
  { value: "Approved", label: "Approved" },
  { value: "Picking", label: "Picking" },
  { value: "Packed", label: "Packed" },
  { value: "Shipped", label: "Shipped" },
];

export default function OrdersPage() {
  return (
    <RequirePermission anyOf={[P.ordersView]}>
      <Suspense fallback={<PageSpinner />}>
        <OrdersView />
      </Suspense>
    </RequirePermission>
  );
}

function OrdersView() {
  const params = useSearchParams();
  const router = useRouter();
  const { hasPermission } = useAuth();
  const toast = useToast();
  const warehouses = useAccessibleWarehouses();
  const [initial] = useState(() => {
    const s = params.get("status") ?? "";
    return {
      status: ((SALES_ORDER_STATUSES as readonly string[]).includes(s) ? s : "") as SalesOrderStatus | "",
      warehouseId: params.get("warehouseId") ?? "",
      customerId: params.get("customerId") ?? "",
      from: "",
      to: "",
    };
  });
  const list = useListParams(initial, 25, params.get("search") ?? "");
  const [customer, setCustomer] = useState<PickedCustomer | null>(null);
  const [exporting, setExporting] = useState(false);
  const f = list.filters;

  const query = {
    page: list.page,
    pageSize: list.pageSize,
    search: list.debouncedSearch || undefined,
    status: f.status || undefined,
    warehouseId: f.warehouseId || undefined,
    customerId: f.customerId || undefined,
    from: f.from || undefined,
    to: f.to || undefined,
  };

  const orders = useQuery({
    queryKey: qk.orders(query),
    queryFn: ({ signal }) => api.get<PagedResult<OrderSummary>>("/api/orders", query, signal),
    placeholderData: keepPreviousData,
  });

  const filtered = !!(query.search || query.status || query.warehouseId || query.customerId || query.from || query.to);
  const clearFilters = () => {
    list.reset();
    for (const k of ["status", "warehouseId", "customerId", "from", "to"] as const) list.setFilter(k, "");
    setCustomer(null);
  };

  const exportCsv = async () => {
    setExporting(true);
    try {
      const { page: _p, pageSize: _s, ...filters } = query;
      const rows = await fetchAllPages<OrderSummary>("/api/orders", filters);
      downloadCsv(
        timestampedName("orders"),
        toCsv(rows, [
          { header: "Number", value: (r) => r.number },
          { header: "Order date", value: (r) => r.orderDate },
          { header: "Required date", value: (r) => r.requiredDate },
          { header: "Customer", value: (r) => r.customerName },
          { header: "Warehouse", value: (r) => r.warehouseCode },
          { header: "Status", value: (r) => r.status },
          { header: "Units", value: (r) => r.totalQuantity },
          { header: "Currency", value: (r) => r.currency },
          { header: "Total", value: (r) => r.total },
          { header: "Required approval", value: (r) => r.requiresApproval },
        ]),
      );
      toast.success(`Exported ${rows.length} orders`);
    } catch (e) {
      toast.error(e);
    } finally {
      setExporting(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Sales orders"
        description="Orders from draft through approval, picking, packing, shipping and returns."
        actions={
          <>
            <Button variant="secondary" icon={<Download className="size-4" />} onClick={exportCsv} loading={exporting} disabled={!orders.data?.totalCount}>
              Export CSV
            </Button>
            {hasPermission(P.ordersCreate) && (
              <ButtonLink href="/orders/new" variant="primary" icon={<Plus className="size-4" />}>
                New order
              </ButtonLink>
            )}
          </>
        }
      />
      <Card>
        <div className="flex gap-1 overflow-x-auto border-b border-slate-200 px-4" role="group" aria-label="Quick status filter">
          {QUICK_TABS.map((t) => (
            <button
              key={t.value || "all"}
              type="button"
              aria-pressed={f.status === t.value}
              onClick={() => list.setFilter("status", t.value)}
              className={cn(
                "-mb-px whitespace-nowrap border-b-2 px-3 py-2.5 text-sm font-medium transition-colors",
                f.status === t.value ? "border-brand-700 text-brand-800" : "border-transparent text-slate-500 hover:border-slate-300 hover:text-slate-700",
              )}
            >
              {t.label}
            </button>
          ))}
        </div>
        <Toolbar>
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Number, customer or reference" label="Search orders" />
          <FilterSelect
            label="Status"
            value={f.status}
            onChange={(v) => list.setFilter("status", v as SalesOrderStatus | "")}
            allLabel="Any status"
            options={SALES_ORDER_STATUSES.map((s) => ({ value: s, label: ORDER_STATUS_LABELS[s] }))}
          />
          <FilterSelect
            label="Warehouse"
            value={f.warehouseId}
            onChange={(v) => list.setFilter("warehouseId", v)}
            allLabel="All warehouses"
            options={(warehouses.data ?? []).map((w) => ({ value: w.id, label: `${w.code} — ${w.name}` }))}
          />
          {hasPermission(P.customersView) && (
            <div className="w-64">
              <CustomerPicker
                label="Customer"
                value={customer ?? (f.customerId ? { id: f.customerId, code: "", name: "Selected customer" } : null)}
                onChange={(c) => {
                  setCustomer(c);
                  list.setFilter("customerId", c?.id ?? "");
                }}
              />
            </div>
          )}
          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-slate-500">Ordered from</span>
            <input type="date" value={f.from} max={f.to || undefined} onChange={(e) => list.setFilter("from", e.target.value)} className={cn(controlClasses, "h-9 w-40")} />
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-slate-500">to</span>
            <input type="date" value={f.to} min={f.from || undefined} onChange={(e) => list.setFilter("to", e.target.value)} className={cn(controlClasses, "h-9 w-40")} />
          </label>
          {filtered && (
            <Button variant="ghost" size="sm" onClick={clearFilters}>
              Clear filters
            </Button>
          )}
        </Toolbar>
        <QueryBoundary
          query={orders}
          columns={8}
          isEmpty={(d) => d.items.length === 0}
          empty={
            filtered ? (
              <NoResults onClear={clearFilters} what="orders" />
            ) : (
              <EmptyState
                icon={<ShoppingCart />}
                title="No orders yet"
                description="Sales orders reserve stock on approval and flow through picking, packing and shipping."
                action={
                  hasPermission(P.ordersCreate) ? (
                    <ButtonLink href="/orders/new" variant="primary" size="sm" icon={<Plus className="size-4" />}>
                      New order
                    </ButtonLink>
                  ) : undefined
                }
              />
            )
          }
        >
          {(data) => (
            <>
              <div className={orders.isPlaceholderData ? "opacity-60 transition-opacity" : undefined}>
                <Table>
                  <THead>
                    <tr>
                      <TH>Order</TH>
                      <TH>Customer</TH>
                      <TH>Warehouse</TH>
                      <TH>Status</TH>
                      <TH>Ordered</TH>
                      <TH>Required</TH>
                      <TH align="right">Units</TH>
                      <TH align="right">Total</TH>
                      <TH>
                        <span className="sr-only">Open</span>
                      </TH>
                    </tr>
                  </THead>
                  <TBody>
                    {data.items.map((o) => {
                      const late =
                        !!o.requiredDate &&
                        !["Shipped", "Delivered", "Cancelled", "Rejected", "Returned"].includes(o.status) &&
                        o.requiredDate < new Date().toISOString().slice(0, 10);
                      return (
                        <TR key={o.id} interactive onClick={() => router.push(`/orders/${o.id}`)}>
                          <TD className="whitespace-nowrap">
                            <Link href={`/orders/${o.id}`} onClick={(e) => e.stopPropagation()} className="font-mono text-xs font-semibold text-slate-900 hover:text-brand-700 hover:underline">
                              {o.number}
                            </Link>
                            {o.requiresApproval && (
                              <Badge tone="neutral" className="ml-2">
                                Approval
                              </Badge>
                            )}
                          </TD>
                          <TD className="max-w-56 truncate font-medium text-slate-800">{o.customerName}</TD>
                          <TD className="font-mono text-xs">{o.warehouseCode}</TD>
                          <TD>
                            <OrderStatusBadge status={o.status} />
                          </TD>
                          <TD className="whitespace-nowrap text-xs text-slate-600">{formatDate(o.orderDate)}</TD>
                          <TD className={cn("whitespace-nowrap text-xs", late ? "font-medium text-red-600" : "text-slate-600")}>
                            {o.requiredDate ? formatDate(o.requiredDate) : "—"}
                            {late && <span className="sr-only"> (overdue)</span>}
                          </TD>
                          <TD numeric>{formatQty(o.totalQuantity)}</TD>
                          <TD numeric className="font-medium text-slate-900">
                            {formatCurrency(o.total, o.currency)}
                          </TD>
                          <TD align="right">
                            <ChevronRight className="size-4 text-slate-400" aria-hidden />
                          </TD>
                        </TR>
                      );
                    })}
                  </TBody>
                </Table>
              </div>
              <Pagination
                page={data.page}
                pageSize={data.pageSize}
                totalCount={data.totalCount}
                totalPages={data.totalPages}
                onPageChange={list.setPage}
                onPageSizeChange={list.setPageSize}
              />
            </>
          )}
        </QueryBoundary>
      </Card>
    </>
  );
}
