"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Lock, Plus, Unlock } from "lucide-react";
import { useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Pagination } from "@/components/ui/Pagination";
import { PageSpinner } from "@/components/ui/Spinner";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { FilterSelect, NoResults, QueryBoundary } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { ReservationStatusBadge } from "@/features/common/StatusBadges";
import { useListParams } from "@/features/common/useListParams";
import { InventoryActionDialog } from "@/features/inventory/InventoryActionDialog";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatDateTime, formatQty, formatRelative, humanize } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useAccessibleWarehouses } from "@/lib/queries";
import { RESERVATION_STATUSES, type PagedResult, type ReservationStatus, type StockReservation } from "@/lib/types";

export default function ReservationsPage() {
  return (
    <RequirePermission anyOf={[P.inventoryView]}>
      <Suspense fallback={<PageSpinner />}>
        <ReservationsView />
      </Suspense>
    </RequirePermission>
  );
}

function ReservationsView() {
  const params = useSearchParams();
  const { hasPermission, canAccessWarehouse } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const warehouses = useAccessibleWarehouses();
  const canReserve = hasPermission(P.inventoryReserve);
  const [initial] = useState(() => ({
    status: ((RESERVATION_STATUSES as readonly string[]).includes(params.get("status") ?? "") ? params.get("status") : "Active") as ReservationStatus | "",
    warehouseId: params.get("warehouseId") ?? "",
  }));
  const list = useListParams(initial);
  const [creating, setCreating] = useState(false);
  const [releasing, setReleasing] = useState<StockReservation | null>(null);

  const query = {
    page: list.page,
    pageSize: list.pageSize,
    status: list.filters.status || undefined,
    warehouseId: list.filters.warehouseId || undefined,
  };

  const reservations = useQuery({
    queryKey: qk.reservations(query),
    queryFn: ({ signal }) => api.get<PagedResult<StockReservation>>("/api/reservations", query, signal),
    placeholderData: keepPreviousData,
  });

  const release = useMutation({
    mutationFn: (id: string) => api.post<StockReservation>(`/api/reservations/${id}/release`),
    onSuccess: (r) => {
      for (const root of ["reservations", "inventory", "movements", "dashboard", "products"]) void queryClient.invalidateQueries({ queryKey: [root] });
      toast.success("Reservation released", `${formatQty(r.quantityReleased)} units of ${r.productName} are available again.`);
    },
  });

  const filtered = !!(query.warehouseId || (query.status && query.status !== "Active"));

  return (
    <>
      <PageHeader
        title="Reservations"
        description="Stock held for transfers, orders or manual holds. Reserved units cannot be issued."
        actions={
          canReserve ? (
            <Button icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
              New hold
            </Button>
          ) : null
        }
      />
      <Card>
        <Toolbar>
          <FilterSelect
            label="Status"
            value={list.filters.status}
            onChange={(v) => list.setFilter("status", v as ReservationStatus | "")}
            allLabel="All statuses"
            options={RESERVATION_STATUSES.map((s) => ({ value: s, label: humanize(s) }))}
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
          query={reservations}
          columns={8}
          isEmpty={(d) => d.items.length === 0}
          empty={
            filtered ? (
              <NoResults
                onClear={() => {
                  list.setFilter("status", "Active");
                  list.setFilter("warehouseId", "");
                }}
                what="reservations"
              />
            ) : (
              <EmptyState
                icon={<Lock />}
                title="No active reservations"
                description="Transfers reserve stock automatically; you can also place a manual hold."
                action={
                  canReserve ? (
                    <Button size="sm" icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
                      New hold
                    </Button>
                  ) : undefined
                }
              />
            )
          }
        >
          {(data) => (
            <>
              <div className={reservations.isPlaceholderData ? "opacity-60 transition-opacity" : undefined}>
                <Table>
                  <THead>
                    <tr>
                      <TH>Product</TH>
                      <TH>Warehouse</TH>
                      <TH>Source</TH>
                      <TH align="right">Reserved</TH>
                      <TH align="right">Consumed</TH>
                      <TH align="right">Released</TH>
                      <TH align="right">Remaining</TH>
                      <TH>Status</TH>
                      <TH>Expires</TH>
                      <TH>Created</TH>
                      {canReserve && (
                        <TH>
                          <span className="sr-only">Actions</span>
                        </TH>
                      )}
                    </tr>
                  </THead>
                  <TBody>
                    {data.items.map((r) => {
                      const manual = r.referenceType === "Manual";
                      return (
                        <TR key={r.id}>
                          <TD className="max-w-64">
                            <div className="truncate font-medium text-slate-900">{r.productName}</div>
                            <div className="font-mono text-xs text-slate-500">{r.sku}</div>
                          </TD>
                          <TD className="font-mono text-xs">{r.warehouseCode}</TD>
                          <TD className="text-xs">
                            <div className="text-slate-700">{manual ? "Manual hold" : humanize(r.referenceType)}</div>
                            {r.referenceNumber && <div className="font-mono text-slate-500">{r.referenceNumber}</div>}
                            {r.notes && (
                              <div className="max-w-48 truncate text-slate-500" title={r.notes}>
                                {r.notes}
                              </div>
                            )}
                          </TD>
                          <TD numeric>{formatQty(r.quantity)}</TD>
                          <TD numeric className="text-slate-500">
                            {formatQty(r.quantityConsumed)}
                          </TD>
                          <TD numeric className="text-slate-500">
                            {formatQty(r.quantityReleased)}
                          </TD>
                          <TD numeric className="font-semibold text-slate-900">
                            {formatQty(r.remainingQuantity)}
                          </TD>
                          <TD>
                            <ReservationStatusBadge status={r.status} />
                          </TD>
                          <TD className="whitespace-nowrap text-xs text-slate-500" title={r.expiresAt ? formatDateTime(r.expiresAt) : undefined}>
                            {r.expiresAt ? formatRelative(r.expiresAt) : "No expiry"}
                          </TD>
                          <TD className="whitespace-nowrap text-xs text-slate-500">{formatDateTime(r.createdAt)}</TD>
                          {canReserve && (
                            <TD align="right">
                              {r.status === "Active" && canAccessWarehouse(r.warehouseId) && (
                                <Button
                                  size="sm"
                                  variant="secondary"
                                  icon={<Unlock className="size-3.5" />}
                                  onClick={() => setReleasing(r)}
                                  title={manual ? undefined : "Releasing a document reservation frees the stock for other uses"}
                                >
                                  Release
                                </Button>
                              )}
                            </TD>
                          )}
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

      <InventoryActionDialog action={creating ? "reserve" : null} onClose={() => setCreating(false)} />
      <ConfirmDialog
        open={!!releasing}
        onClose={() => setReleasing(null)}
        title="Release reservation?"
        description={
          releasing
            ? `${formatQty(releasing.remainingQuantity)} units of ${releasing.productName} at ${releasing.warehouseCode} become available again.${
                releasing.referenceType !== "Manual" ? ` This hold belongs to ${humanize(releasing.referenceType).toLowerCase()} ${releasing.referenceNumber ?? ""}.` : ""
              }`
            : undefined
        }
        confirmLabel="Release"
        onConfirm={() => (releasing ? release.mutateAsync(releasing.id) : undefined)}
      />
    </>
  );
}
