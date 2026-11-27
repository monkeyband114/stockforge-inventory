"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ArrowLeftRight, ArrowRight, ChevronRight, Plus } from "lucide-react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { PageSpinner } from "@/components/ui/Spinner";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { FilterSelect, NoResults, QueryBoundary, SearchInput } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { TransferStatusBadge } from "@/features/common/StatusBadges";
import { useListParams } from "@/features/common/useListParams";
import { NewTransferDialog } from "@/features/inventory/NewTransferDialog";
import { TransferDrawer } from "@/features/inventory/TransferDrawer";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatDateTime, formatQty } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useAccessibleWarehouses } from "@/lib/queries";
import { TRANSFER_STATUSES, type PagedResult, type StockTransfer, type TransferStatus } from "@/lib/types";

const STATUS_LABELS: Record<TransferStatus, string> = {
  Requested: "Requested",
  InTransit: "In transit",
  Completed: "Completed",
  Cancelled: "Cancelled",
};

export default function TransfersPage() {
  return (
    <RequirePermission anyOf={[P.inventoryView]}>
      <Suspense fallback={<PageSpinner />}>
        <TransfersView />
      </Suspense>
    </RequirePermission>
  );
}

function TransfersView() {
  const params = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const { hasPermission } = useAuth();
  const warehouses = useAccessibleWarehouses();
  const [initial] = useState(() => ({
    status: (TRANSFER_STATUSES as readonly string[]).includes(params.get("status") ?? "") ? (params.get("status") as TransferStatus) : ("" as TransferStatus | ""),
    warehouseId: params.get("warehouseId") ?? "",
  }));
  const list = useListParams(initial);
  const [creating, setCreating] = useState(false);
  const selectedId = params.get("id");

  const openTransfer = (id: string | null) => {
    const next = new URLSearchParams(params.toString());
    if (id) next.set("id", id);
    else next.delete("id");
    const qs = next.toString();
    router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
  };

  const query = {
    page: list.page,
    pageSize: list.pageSize,
    search: list.debouncedSearch || undefined,
    status: list.filters.status || undefined,
    warehouseId: list.filters.warehouseId || undefined,
  };

  const transfers = useQuery({
    queryKey: qk.transfers(query),
    queryFn: ({ signal }) => api.get<PagedResult<StockTransfer>>("/api/stock-transfers", query, signal),
    placeholderData: keepPreviousData,
  });

  const filtered = !!(query.search || query.status || query.warehouseId);
  const clearFilters = () => {
    list.reset();
    list.setFilter("status", "");
    list.setFilter("warehouseId", "");
  };

  return (
    <>
      <PageHeader
        title="Stock transfers"
        description="Inter-warehouse moves: requested → dispatched (in transit) → received."
        actions={
          hasPermission(P.inventoryTransfer) ? (
            <Button icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
              New transfer
            </Button>
          ) : null
        }
      />
      <Card>
        <Toolbar>
          <SearchInput value={list.search} onChange={list.setSearch} placeholder="Transfer number" label="Search transfers" />
          <FilterSelect
            label="Status"
            value={list.filters.status}
            onChange={(v) => list.setFilter("status", v as TransferStatus | "")}
            allLabel="All statuses"
            options={TRANSFER_STATUSES.map((s) => ({ value: s, label: STATUS_LABELS[s] }))}
          />
          <FilterSelect
            label="Warehouse"
            value={list.filters.warehouseId}
            onChange={(v) => list.setFilter("warehouseId", v)}
            allLabel="All warehouses"
            options={(warehouses.data ?? []).map((w) => ({ value: w.id, label: `${w.code} — ${w.name}` }))}
          />
        </Toolbar>
        <QueryBoundary
          query={transfers}
          columns={7}
          isEmpty={(d) => d.items.length === 0}
          empty={
            filtered ? (
              <NoResults onClear={clearFilters} what="transfers" />
            ) : (
              <EmptyState
                icon={<ArrowLeftRight />}
                title="No transfers yet"
                description="Move stock between warehouses with a transfer request."
                action={
                  hasPermission(P.inventoryTransfer) ? (
                    <Button size="sm" icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
                      New transfer
                    </Button>
                  ) : undefined
                }
              />
            )
          }
        >
          {(data) => (
            <>
              <div className={transfers.isPlaceholderData ? "opacity-60 transition-opacity" : undefined}>
                <Table>
                  <THead>
                    <tr>
                      <TH>Number</TH>
                      <TH>Route</TH>
                      <TH>Status</TH>
                      <TH align="right">Lines</TH>
                      <TH align="right">Units</TH>
                      <TH>Requested</TH>
                      <TH>Last update</TH>
                      <TH>
                        <span className="sr-only">Open</span>
                      </TH>
                    </tr>
                  </THead>
                  <TBody>
                    {data.items.map((t) => (
                      <TR
                        key={t.id}
                        interactive
                        onClick={() => openTransfer(t.id)}
                        className={selectedId === t.id ? "bg-brand-50/50" : undefined}
                      >
                        <TD className="whitespace-nowrap font-mono text-xs font-semibold text-slate-900">
                          <button
                            type="button"
                            onClick={(e) => {
                              e.stopPropagation();
                              openTransfer(t.id);
                            }}
                            className="hover:text-brand-700 hover:underline"
                          >
                            {t.number}
                          </button>
                        </TD>
                        <TD>
                          <span className="inline-flex items-center gap-1.5 font-mono text-xs">
                            <span title={t.source.name}>{t.source.code}</span>
                            <ArrowRight className="size-3 text-slate-400" aria-label="to" />
                            <span title={t.destination.name}>{t.destination.code}</span>
                          </span>
                        </TD>
                        <TD>
                          <TransferStatusBadge status={t.status} />
                        </TD>
                        <TD numeric>{t.lines.length}</TD>
                        <TD numeric className="font-medium text-slate-900">
                          {formatQty(t.totalQuantity)}
                        </TD>
                        <TD className="whitespace-nowrap text-xs">
                          <div className="text-slate-700">{t.requestedByName ?? "—"}</div>
                          <div className="text-slate-500">{formatDateTime(t.requestedAt)}</div>
                        </TD>
                        <TD className="whitespace-nowrap text-xs text-slate-500">
                          {formatDateTime(t.receivedAt ?? t.cancelledAt ?? t.dispatchedAt ?? t.requestedAt)}
                        </TD>
                        <TD align="right">
                          <ChevronRight className="size-4 text-slate-400" aria-hidden />
                        </TD>
                      </TR>
                    ))}
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

      <NewTransferDialog open={creating} onClose={() => setCreating(false)} onCreated={(t) => openTransfer(t.id)} />
      <TransferDrawer transferId={selectedId} onClose={() => openTransfer(null)} />
    </>
  );
}
