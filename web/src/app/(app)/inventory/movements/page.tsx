"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Download, History } from "lucide-react";
import { useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { controlClasses } from "@/components/ui/Field";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { PageSpinner } from "@/components/ui/Spinner";
import { useToast } from "@/components/ui/Toast";
import { FilterSelect, NoResults, QueryBoundary, SearchInput } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { MOVEMENT_LABELS } from "@/features/common/StatusBadges";
import { useListParams } from "@/features/common/useListParams";
import { MovementsTable } from "@/features/inventory/MovementsTable";
import { api, fetchAllPages } from "@/lib/api";
import { cn } from "@/lib/cn";
import { downloadCsv, timestampedName, toCsv } from "@/lib/csv";
import { endOfDayExclusiveIso, startOfDayIso } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useAccessibleWarehouses, useCurrency } from "@/lib/queries";
import { STOCK_MOVEMENT_TYPES, type PagedResult, type StockMovement, type StockMovementType } from "@/lib/types";

export default function MovementsPage() {
  return (
    <RequirePermission anyOf={[P.inventoryView]}>
      <Suspense fallback={<PageSpinner />}>
        <MovementsView />
      </Suspense>
    </RequirePermission>
  );
}

function DateFilter({ label, value, onChange, min, max }: { label: string; value: string; onChange: (v: string) => void; min?: string; max?: string }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-xs font-medium text-slate-500">{label}</span>
      <input type="date" value={value} min={min} max={max} onChange={(e) => onChange(e.target.value)} className={cn(controlClasses, "h-9 w-40")} />
    </label>
  );
}

function MovementsView() {
  const params = useSearchParams();
  const toast = useToast();
  const currency = useCurrency();
  const warehouses = useAccessibleWarehouses();
  const [initial] = useState(() => ({
    warehouseId: params.get("warehouseId") ?? "",
    type: (params.get("type") ?? "") as StockMovementType | "",
    from: params.get("from") ?? "",
    to: params.get("to") ?? "",
    productId: params.get("productId") ?? "",
  }));
  const list = useListParams(initial, 50, params.get("search") ?? "");
  const [exporting, setExporting] = useState(false);
  const f = list.filters;

  const query = {
    page: list.page,
    pageSize: list.pageSize,
    search: list.debouncedSearch || undefined,
    warehouseId: f.warehouseId || undefined,
    productId: f.productId || undefined,
    type: f.type || undefined,
    from: f.from ? startOfDayIso(f.from) : undefined,
    to: f.to ? endOfDayExclusiveIso(f.to) : undefined,
  };

  const movements = useQuery({
    queryKey: qk.movements(query),
    queryFn: ({ signal }) => api.get<PagedResult<StockMovement>>("/api/stock-movements", query, signal),
    placeholderData: keepPreviousData,
  });

  const filtered = !!(query.search || query.warehouseId || query.type || query.from || query.to || query.productId);
  const clearFilters = () => {
    list.reset();
    for (const key of ["warehouseId", "type", "from", "to", "productId"] as const) list.setFilter(key, "");
  };

  const exportCsv = async () => {
    setExporting(true);
    try {
      const { page: _p, pageSize: _s, ...filters } = query;
      const rows = await fetchAllPages<StockMovement>("/api/stock-movements", filters);
      const csv = toCsv(rows, [
        { header: "Timestamp", value: (r) => r.createdAt },
        { header: "Type", value: (r) => r.type },
        { header: "Warehouse", value: (r) => r.warehouseCode },
        { header: "SKU", value: (r) => r.sku },
        { header: "Product", value: (r) => r.productName },
        { header: "Quantity change", value: (r) => r.quantityDelta },
        { header: "Reserved change", value: (r) => r.reservedDelta },
        { header: "On hand after", value: (r) => r.onHandAfter },
        { header: "Reserved after", value: (r) => r.reservedAfter },
        { header: `Unit cost (${currency})`, value: (r) => r.unitCost },
        { header: "Reference type", value: (r) => r.referenceType },
        { header: "Reference number", value: (r) => r.referenceNumber },
        { header: "Reason", value: (r) => r.reason },
        { header: "Created by", value: (r) => r.createdByName },
      ]);
      downloadCsv(timestampedName("stock-movements"), csv);
      toast.success(`Exported ${rows.length} movements`);
    } catch (e) {
      toast.error(e);
    } finally {
      setExporting(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Stock movements"
        description="The append-only ledger of every change to on-hand and reserved stock."
        actions={
          <Button variant="secondary" icon={<Download className="size-4" />} onClick={exportCsv} loading={exporting} disabled={!movements.data?.totalCount}>
            Export CSV
          </Button>
        }
      />
      <Card>
        <Toolbar>
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Product, SKU or reference" label="Search movements" />
          <FilterSelect
            label="Movement type"
            value={f.type}
            onChange={(v) => list.setFilter("type", v as StockMovementType | "")}
            allLabel="All types"
            options={STOCK_MOVEMENT_TYPES.map((t) => ({ value: t, label: MOVEMENT_LABELS[t] }))}
          />
          <FilterSelect
            label="Warehouse"
            value={f.warehouseId}
            onChange={(v) => list.setFilter("warehouseId", v)}
            allLabel="All warehouses"
            options={(warehouses.data ?? []).map((w) => ({ value: w.id, label: `${w.code} — ${w.name}` }))}
          />
          <DateFilter label="From" value={f.from} onChange={(v) => list.setFilter("from", v)} max={f.to || undefined} />
          <DateFilter label="To" value={f.to} onChange={(v) => list.setFilter("to", v)} min={f.from || undefined} />
          {filtered && (
            <Button variant="ghost" size="sm" onClick={clearFilters} className="mb-0.5">
              Clear filters
            </Button>
          )}
        </Toolbar>
        <QueryBoundary
          query={movements}
          columns={9}
          isEmpty={(d) => d.items.length === 0}
          empty={
            filtered ? (
              <NoResults onClear={clearFilters} what="movements" />
            ) : (
              <EmptyState icon={<History />} title="No movements yet" description="Receipts, issues, transfers, adjustments and reservations will be listed here." />
            )
          }
        >
          {(data) => (
            <>
              <div className={movements.isPlaceholderData ? "opacity-60 transition-opacity" : undefined}>
                <MovementsTable movements={data.items} currency={currency} />
              </div>
              <Pagination
                page={data.page}
                pageSize={data.pageSize}
                totalCount={data.totalCount}
                totalPages={data.totalPages}
                onPageChange={list.setPage}
                onPageSizeChange={list.setPageSize}
                pageSizes={[25, 50, 100, 200]}
              />
            </>
          )}
        </QueryBoundary>
      </Card>
    </>
  );
}
