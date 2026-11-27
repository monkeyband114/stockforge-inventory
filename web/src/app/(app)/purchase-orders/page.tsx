"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ChevronRight, ClipboardList, Download, Lightbulb, Plus } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Button, ButtonLink } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { ToggleChip } from "@/components/ui/Checkbox";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { PageSpinner } from "@/components/ui/Spinner";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { FilterSelect, NoResults, QueryBoundary, SearchInput } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { useListParams } from "@/features/common/useListParams";
import { PO_STATUS_LABELS, PurchaseOrderStatusBadge } from "@/features/procurement/ProcurementBadges";
import { SupplierPicker, type PickedSupplier } from "@/features/procurement/SupplierPicker";
import { api, fetchAllPages } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { downloadCsv, timestampedName, toCsv } from "@/lib/csv";
import { formatCurrency, formatDate, formatQty } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useWarehouses } from "@/lib/queries";
import { PURCHASE_ORDER_STATUSES, type PagedResult, type PurchaseOrderStatus, type PurchaseOrderSummary } from "@/lib/types";

const TABS: { value: PurchaseOrderStatus | ""; label: string }[] = [
  { value: "", label: "All" },
  { value: "Draft", label: "Drafts" },
  { value: "PendingApproval", label: "Pending approval" },
  { value: "Approved", label: "Awaiting delivery" },
  { value: "PartiallyReceived", label: "Partially received" },
  { value: "Received", label: "Received" },
];

export default function PurchaseOrdersPage() {
  return (
    <RequirePermission anyOf={[P.purchasingView]}>
      <Suspense fallback={<PageSpinner />}>
        <PurchaseOrdersView />
      </Suspense>
    </RequirePermission>
  );
}

function PurchaseOrdersView() {
  const params = useSearchParams();
  const router = useRouter();
  const { hasPermission } = useAuth();
  const toast = useToast();
  const warehouses = useWarehouses();
  const [initial] = useState(() => {
    const s = params.get("status") ?? "";
    return {
      status: ((PURCHASE_ORDER_STATUSES as readonly string[]).includes(s) ? s : "") as PurchaseOrderStatus | "",
      supplierId: params.get("supplierId") ?? "",
      warehouseId: params.get("warehouseId") ?? "",
      outstandingOnly: ["true", "1"].includes(params.get("outstanding") ?? ""),
    };
  });
  const list = useListParams(initial, 25, params.get("search") ?? "");
  const [supplier, setSupplier] = useState<PickedSupplier | null>(null);
  const [exporting, setExporting] = useState(false);
  const f = list.filters;

  const query = {
    page: list.page,
    pageSize: list.pageSize,
    search: list.debouncedSearch || undefined,
    status: f.status || undefined,
    supplierId: f.supplierId || undefined,
    warehouseId: f.warehouseId || undefined,
    outstandingOnly: f.outstandingOnly || undefined,
  };
  const orders = useQuery({
    queryKey: qk.purchaseOrders(query),
    queryFn: ({ signal }) => api.get<PagedResult<PurchaseOrderSummary>>("/api/purchase-orders", query, signal),
    placeholderData: keepPreviousData,
  });

  const filtered = !!(query.search || query.status || query.supplierId || query.warehouseId || query.outstandingOnly);
  const clearFilters = () => {
    list.reset();
    list.setFilter("status", "");
    list.setFilter("supplierId", "");
    list.setFilter("warehouseId", "");
    list.setFilter("outstandingOnly", false);
    setSupplier(null);
  };

  const exportCsv = async () => {
    setExporting(true);
    try {
      const { page: _p, pageSize: _s, ...filters } = query;
      const rows = await fetchAllPages<PurchaseOrderSummary>("/api/purchase-orders", filters);
      downloadCsv(
        timestampedName("purchase-orders"),
        toCsv(rows, [
          { header: "Number", value: (r) => r.number },
          { header: "Order date", value: (r) => r.orderDate },
          { header: "Expected", value: (r) => r.expectedDate },
          { header: "Supplier", value: (r) => r.supplierName },
          { header: "Warehouse", value: (r) => r.warehouseCode },
          { header: "Status", value: (r) => r.status },
          { header: "Ordered", value: (r) => r.quantityOrdered },
          { header: "Received", value: (r) => r.quantityReceived },
          { header: "Currency", value: (r) => r.currency },
          { header: "Total", value: (r) => r.total },
        ]),
      );
      toast.success(`Exported ${rows.length} purchase orders`);
    } catch (e) {
      toast.error(e);
    } finally {
      setExporting(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Purchase orders"
        description="Orders to suppliers from draft through approval to goods receipt."
        actions={
          <>
            <Button variant="secondary" icon={<Download className="size-4" />} onClick={exportCsv} loading={exporting} disabled={!orders.data?.totalCount}>
              Export CSV
            </Button>
            {hasPermission(P.purchasingCreate) && (
              <>
                <ButtonLink href="/purchase-orders/suggestions" icon={<Lightbulb className="size-4" />}>
                  Reorder suggestions
                </ButtonLink>
                <ButtonLink href="/purchase-orders/new" variant="primary" icon={<Plus className="size-4" />}>
                  New purchase order
                </ButtonLink>
              </>
            )}
          </>
        }
      />
      <Card>
        <div className="flex gap-1 overflow-x-auto border-b border-slate-200 px-4" role="group" aria-label="Quick status filter">
          {TABS.map((t) => (
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
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="PO number or supplier" label="Search purchase orders" />
          <FilterSelect
            label="Status"
            value={f.status}
            onChange={(v) => list.setFilter("status", v as PurchaseOrderStatus | "")}
            allLabel="Any status"
            options={PURCHASE_ORDER_STATUSES.map((s) => ({ value: s, label: PO_STATUS_LABELS[s] }))}
          />
          <FilterSelect
            label="Warehouse"
            value={f.warehouseId}
            onChange={(v) => list.setFilter("warehouseId", v)}
            allLabel="All warehouses"
            options={(warehouses.data ?? []).map((w) => ({ value: w.id, label: `${w.code} — ${w.name}` }))}
          />
          {hasPermission(P.suppliersView) && (
            <div className="w-64">
              <SupplierPicker
                value={supplier ?? (f.supplierId ? { id: f.supplierId, code: "", name: "Selected supplier" } : null)}
                onChange={(s) => {
                  setSupplier(s);
                  list.setFilter("supplierId", s?.id ?? "");
                }}
              />
            </div>
          )}
          <ToggleChip checked={f.outstandingOnly} onChange={(v) => list.setFilter("outstandingOnly", v)}>
            Outstanding deliveries
          </ToggleChip>
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
              <NoResults onClear={clearFilters} what="purchase orders" />
            ) : (
              <EmptyState
                icon={<ClipboardList />}
                title="No purchase orders yet"
                description="Raise a purchase order, or start from the reorder suggestions."
                action={
                  hasPermission(P.purchasingCreate) ? (
                    <ButtonLink href="/purchase-orders/suggestions" size="sm" icon={<Lightbulb className="size-4" />}>
                      Reorder suggestions
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
                      <TH>Supplier</TH>
                      <TH>Warehouse</TH>
                      <TH>Status</TH>
                      <TH>Ordered</TH>
                      <TH>Expected</TH>
                      <TH>Received</TH>
                      <TH align="right">Total</TH>
                      <TH>
                        <span className="sr-only">Open</span>
                      </TH>
                    </tr>
                  </THead>
                  <TBody>
                    {data.items.map((po) => {
                      const pct = po.quantityOrdered > 0 ? Math.min(100, (po.quantityReceived / po.quantityOrdered) * 100) : 0;
                      const late = !!po.expectedDate && ["Approved", "PartiallyReceived"].includes(po.status) && po.expectedDate < new Date().toISOString().slice(0, 10);
                      return (
                        <TR key={po.id} interactive onClick={() => router.push(`/purchase-orders/${po.id}`)}>
                          <TD>
                            <Link href={`/purchase-orders/${po.id}`} onClick={(e) => e.stopPropagation()} className="font-mono text-xs font-semibold text-slate-900 hover:text-brand-700 hover:underline">
                              {po.number}
                            </Link>
                          </TD>
                          <TD className="max-w-56 truncate font-medium text-slate-800">{po.supplierName}</TD>
                          <TD className="font-mono text-xs">{po.warehouseCode}</TD>
                          <TD>
                            <PurchaseOrderStatusBadge status={po.status} />
                          </TD>
                          <TD className="whitespace-nowrap text-xs text-slate-600">{formatDate(po.orderDate)}</TD>
                          <TD className={cn("whitespace-nowrap text-xs", late ? "font-semibold text-red-600" : "text-slate-600")}>
                            {po.expectedDate ? formatDate(po.expectedDate) : "—"}
                            {late && " · late"}
                          </TD>
                          <TD>
                            <div className="w-32">
                              <div className="flex justify-between text-[11px] tabular-nums text-slate-500">
                                <span>{formatQty(po.quantityReceived)}</span>
                                <span>/ {formatQty(po.quantityOrdered)}</span>
                              </div>
                              <div
                                className="mt-1 h-1.5 overflow-hidden rounded-full bg-slate-100"
                                role="progressbar"
                                aria-label={`Received ${formatQty(po.quantityReceived)} of ${formatQty(po.quantityOrdered)}`}
                                aria-valuemin={0}
                                aria-valuemax={po.quantityOrdered}
                                aria-valuenow={po.quantityReceived}
                              >
                                <div className="h-full rounded-full bg-emerald-500" style={{ width: `${pct}%` }} />
                              </div>
                            </div>
                          </TD>
                          <TD numeric className="font-medium text-slate-900">
                            {formatCurrency(po.total, po.currency)}
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
