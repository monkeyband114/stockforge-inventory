"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, ClipboardCheck, ClipboardList, PencilRuler, X } from "lucide-react";
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
import { Tabs } from "@/components/ui/Tabs";
import { useToast } from "@/components/ui/Toast";
import { FilterSelect, NoResults, QueryBoundary } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { AdjustmentStatusBadge, SignedQty } from "@/features/common/StatusBadges";
import { useListParams } from "@/features/common/useListParams";
import { InventoryActionDialog, type InventoryAction } from "@/features/inventory/InventoryActionDialog";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatDateTime, formatQty, humanize } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useAccessibleWarehouses } from "@/lib/queries";
import type { AdjustmentStatus, PagedResult, StockAdjustment } from "@/lib/types";

type StatusTab = AdjustmentStatus | "all";

export default function AdjustmentsPage() {
  return (
    <RequirePermission anyOf={[P.inventoryView]}>
      <Suspense fallback={<PageSpinner />}>
        <AdjustmentsView />
      </Suspense>
    </RequirePermission>
  );
}

function AdjustmentsView() {
  const params = useSearchParams();
  const { user, hasPermission } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const warehouses = useAccessibleWarehouses();
  const canApprove = hasPermission(P.inventoryAdjustApprove);
  const initialStatus = params.get("status");
  const [initial] = useState(() => ({
    status: (["PendingApproval", "Applied", "Rejected"].includes(initialStatus ?? "") ? initialStatus : canApprove ? "PendingApproval" : "all") as StatusTab,
    warehouseId: params.get("warehouseId") ?? "",
  }));
  const list = useListParams(initial);
  const [review, setReview] = useState<{ adjustment: StockAdjustment; decision: "approve" | "reject" } | null>(null);
  const [action, setAction] = useState<InventoryAction | null>(null);

  const query = {
    page: list.page,
    pageSize: list.pageSize,
    status: list.filters.status === "all" ? undefined : list.filters.status,
    warehouseId: list.filters.warehouseId || undefined,
  };

  const adjustments = useQuery({
    queryKey: qk.adjustments(query),
    queryFn: ({ signal }) => api.get<PagedResult<StockAdjustment>>("/api/stock-adjustments", query, signal),
    placeholderData: keepPreviousData,
  });

  const pendingCount = useQuery({
    queryKey: qk.adjustments({ status: "PendingApproval", warehouseId: query.warehouseId, pageSize: 1, countOnly: true }),
    queryFn: () => api.get<PagedResult<StockAdjustment>>("/api/stock-adjustments", { status: "PendingApproval", warehouseId: query.warehouseId, pageSize: 1 }),
  });

  const reviewMutation = useMutation({
    mutationFn: ({ id, decision, notes }: { id: string; decision: "approve" | "reject"; notes: string }) =>
      api.post<StockAdjustment>(`/api/stock-adjustments/${id}/${decision}`, { notes: notes || null }),
    onSuccess: (adj, vars) => {
      for (const root of ["adjustments", "inventory", "movements", "dashboard", "products"]) void queryClient.invalidateQueries({ queryKey: [root] });
      toast.success(vars.decision === "approve" ? `Adjustment ${adj.number} approved and applied` : `Adjustment ${adj.number} rejected`);
    },
  });

  const tabs = [
    { value: "PendingApproval" as const, label: "Pending approval", count: pendingCount.data?.totalCount },
    { value: "Applied" as const, label: "Applied" },
    { value: "Rejected" as const, label: "Rejected" },
    { value: "all" as const, label: "All" },
  ];

  return (
    <>
      <PageHeader
        title="Stock adjustments"
        description="Cycle-count variances and corrections. Pending requests change stock only once approved."
        actions={
          <>
            {hasPermission(P.inventoryCount) && (
              <Button variant="secondary" icon={<ClipboardList className="size-4" />} onClick={() => setAction("count")}>
                Cycle count
              </Button>
            )}
            {hasPermission(P.inventoryAdjustRequest) && (
              <Button icon={<PencilRuler className="size-4" />} onClick={() => setAction("correction")}>
                Request correction
              </Button>
            )}
          </>
        }
      />
      <Card>
        <Tabs tabs={tabs} value={list.filters.status} onChange={(v) => list.setFilter("status", v)} className="px-4" />
        <Toolbar>
          <FilterSelect
            label="Warehouse"
            value={list.filters.warehouseId}
            onChange={(v) => list.setFilter("warehouseId", v)}
            allLabel="All warehouses"
            options={(warehouses.data ?? []).map((w) => ({ value: w.id, label: `${w.code} — ${w.name}` }))}
          />
          {canApprove && (
            <p className="mb-2 text-xs text-slate-500">You can review requests from other members; your own requests must be reviewed by someone else.</p>
          )}
        </Toolbar>
        <QueryBoundary
          query={adjustments}
          columns={9}
          isEmpty={(d) => d.items.length === 0}
          empty={
            list.filters.warehouseId ? (
              <NoResults onClear={() => list.setFilter("warehouseId", "")} what="adjustments" />
            ) : (
              <EmptyState
                icon={<ClipboardCheck />}
                title={list.filters.status === "PendingApproval" ? "Nothing awaiting approval" : "No adjustments"}
                description={list.filters.status === "PendingApproval" ? "New counts and corrections that need review will appear here in real time." : "Counts and corrections will be listed here."}
              />
            )
          }
        >
          {(data) => (
            <>
              <div className={adjustments.isPlaceholderData ? "opacity-60 transition-opacity" : undefined}>
                <Table>
                  <THead>
                    <tr>
                      <TH>Number</TH>
                      <TH>Product</TH>
                      <TH>Warehouse</TH>
                      <TH>Reason</TH>
                      <TH align="right">Change</TH>
                      <TH align="right">System qty</TH>
                      <TH align="right">Counted</TH>
                      <TH>Status</TH>
                      <TH>Requested</TH>
                      <TH>Review</TH>
                      {canApprove && (
                        <TH>
                          <span className="sr-only">Actions</span>
                        </TH>
                      )}
                    </tr>
                  </THead>
                  <TBody>
                    {data.items.map((a) => {
                      const own = a.requestedBy === user?.id;
                      return (
                        <TR key={a.id}>
                          <TD className="whitespace-nowrap font-mono text-xs font-semibold text-slate-900">{a.number}</TD>
                          <TD className="max-w-64">
                            <div className="truncate font-medium text-slate-900">{a.productName}</div>
                            <div className="font-mono text-xs text-slate-500">{a.sku}</div>
                          </TD>
                          <TD className="font-mono text-xs">{a.warehouseCode}</TD>
                          <TD>
                            <div className="text-slate-700">{humanize(a.reason)}</div>
                            {a.notes && (
                              <div className="max-w-56 truncate text-xs text-slate-500" title={a.notes}>
                                {a.notes}
                              </div>
                            )}
                          </TD>
                          <TD numeric>
                            <SignedQty value={a.quantityChange} />
                          </TD>
                          <TD numeric>{formatQty(a.systemQuantity)}</TD>
                          <TD numeric>{a.countedQuantity !== null ? formatQty(a.countedQuantity) : "—"}</TD>
                          <TD>
                            <AdjustmentStatusBadge status={a.status} />
                          </TD>
                          <TD className="whitespace-nowrap text-xs">
                            <div className="text-slate-700">{a.requestedByName ?? "Unknown"}</div>
                            <div className="text-slate-500">{formatDateTime(a.requestedAt)}</div>
                          </TD>
                          <TD className="text-xs">
                            {a.reviewedAt ? (
                              <>
                                <div className="text-slate-700">{a.reviewedByName ?? "—"}</div>
                                <div className="text-slate-500">{formatDateTime(a.reviewedAt)}</div>
                                {a.reviewNotes && (
                                  <div className="max-w-48 truncate text-slate-500" title={a.reviewNotes}>
                                    “{a.reviewNotes}”
                                  </div>
                                )}
                              </>
                            ) : (
                              <span className="text-slate-400">—</span>
                            )}
                          </TD>
                          {canApprove && (
                            <TD align="right" className="whitespace-nowrap">
                              {a.status === "PendingApproval" &&
                                (own ? (
                                  <span className="text-xs text-slate-400" title="The API does not allow approving your own request.">
                                    Your request
                                  </span>
                                ) : (
                                  <div className="flex justify-end gap-1.5">
                                    <Button size="sm" variant="secondary" icon={<X className="size-3.5" />} onClick={() => setReview({ adjustment: a, decision: "reject" })}>
                                      Reject
                                    </Button>
                                    <Button size="sm" icon={<Check className="size-3.5" />} onClick={() => setReview({ adjustment: a, decision: "approve" })}>
                                      Approve
                                    </Button>
                                  </div>
                                ))}
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

      <ConfirmDialog
        open={!!review}
        onClose={() => setReview(null)}
        title={review?.decision === "approve" ? `Approve ${review.adjustment.number}?` : `Reject ${review?.adjustment.number ?? ""}?`}
        description={
          review
            ? review.decision === "approve"
              ? `This applies a change of ${formatQty(review.adjustment.quantityChange)} to ${review.adjustment.productName} at ${review.adjustment.warehouseCode} immediately.`
              : `The request is closed without changing stock for ${review.adjustment.productName}.`
            : undefined
        }
        confirmLabel={review?.decision === "approve" ? "Approve & apply" : "Reject request"}
        tone={review?.decision === "reject" ? "danger" : "primary"}
        notes={
          review?.decision === "reject"
            ? { label: "Reason for rejection", required: true, placeholder: "Explain why this adjustment is rejected", maxLength: 1000 }
            : { label: "Review notes (optional)", maxLength: 1000 }
        }
        onConfirm={(notes) => (review ? reviewMutation.mutateAsync({ id: review.adjustment.id, decision: review.decision, notes }) : undefined)}
      />

      <InventoryActionDialog action={action} onClose={() => setAction(null)} />
    </>
  );
}
