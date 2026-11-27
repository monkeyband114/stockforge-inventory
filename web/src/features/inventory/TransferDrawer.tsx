"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowRight, Ban, CheckCircle2, Circle, PackageCheck, Truck, XCircle } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Drawer } from "@/components/ui/Drawer";
import { ErrorState } from "@/components/ui/ErrorState";
import { Skeleton } from "@/components/ui/Skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { TransferStatusBadge } from "@/features/common/StatusBadges";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { formatCurrency, formatDateTime, formatQty } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useCurrency } from "@/lib/queries";
import type { StockTransfer } from "@/lib/types";

type TransferAction = "dispatch" | "receive" | "cancel";

const ACTION_COPY: Record<TransferAction, { title: string; confirm: string; body: (t: StockTransfer) => string; done: string }> = {
  dispatch: {
    title: "Dispatch transfer",
    confirm: "Dispatch",
    body: (t) => `Stock leaves ${t.source.code} now and is marked in transit to ${t.destination.code}.`,
    done: "dispatched",
  },
  receive: {
    title: "Receive transfer",
    confirm: "Receive",
    body: (t) => `${formatQty(t.totalQuantity)} units will be added to ${t.destination.code}'s stock.`,
    done: "received",
  },
  cancel: {
    title: "Cancel transfer",
    confirm: "Cancel transfer",
    body: (t) => `The reservation at ${t.source.code} is released. This cannot be undone.`,
    done: "cancelled",
  },
};

function Timeline({ t }: { t: StockTransfer }) {
  const steps =
    t.status === "Cancelled"
      ? [
          { label: "Requested", at: t.requestedAt, by: t.requestedByName, icon: Circle, done: true },
          { label: "Cancelled", at: t.cancelledAt, icon: XCircle, done: true, danger: true },
        ]
      : [
          { label: "Requested", at: t.requestedAt, by: t.requestedByName, icon: Circle, done: true },
          { label: "Dispatched", at: t.dispatchedAt, icon: Truck, done: !!t.dispatchedAt },
          { label: "Received", at: t.receivedAt, icon: PackageCheck, done: !!t.receivedAt },
        ];
  return (
    <ol className="relative space-y-4" aria-label="Status timeline">
      {steps.map((s, i) => {
        const Icon = s.icon;
        return (
          <li key={s.label} className="relative flex gap-3">
            {i < steps.length - 1 && <span className={cn("absolute left-[11px] top-6 h-[calc(100%-4px)] w-px", s.done ? "bg-brand-600/40" : "bg-slate-200")} aria-hidden="true" />}
            <span
              className={cn(
                "relative z-10 flex size-6 shrink-0 items-center justify-center rounded-full",
                "danger" in s && s.danger ? "bg-red-50 text-red-600" : s.done ? "bg-brand-50 text-brand-700" : "bg-slate-100 text-slate-400",
              )}
            >
              <Icon className="size-3.5" aria-hidden />
            </span>
            <div>
              <p className={cn("text-sm font-medium", s.done ? "text-slate-900" : "text-slate-400")}>
                {s.label}
                <span className="sr-only">{s.done ? " (done)" : " (pending)"}</span>
              </p>
              <p className="text-xs text-slate-500">
                {s.at ? formatDateTime(s.at) : "Pending"}
                {"by" in s && s.by ? ` · ${s.by}` : ""}
              </p>
            </div>
          </li>
        );
      })}
    </ol>
  );
}

export function TransferDrawer({ transferId, onClose }: { transferId: string | null; onClose: () => void }) {
  const { hasPermission, canAccessWarehouse } = useAuth();
  const currency = useCurrency();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [pending, setPending] = useState<TransferAction | null>(null);

  const query = useQuery({
    queryKey: qk.transfer(transferId ?? ""),
    queryFn: () => api.get<StockTransfer>(`/api/stock-transfers/${transferId}`),
    enabled: !!transferId,
  });
  const t = query.data;

  const mutation = useMutation({
    mutationFn: ({ id, action }: { id: string; action: TransferAction }) => api.post<StockTransfer>(`/api/stock-transfers/${id}/${action}`),
    onSuccess: (updated, vars) => {
      queryClient.setQueryData(qk.transfer(updated.id), updated);
      for (const root of ["transfers", "inventory", "movements", "reservations", "dashboard", "products"]) void queryClient.invalidateQueries({ queryKey: [root] });
      toast.success(`Transfer ${updated.number} ${ACTION_COPY[vars.action].done}`);
    },
  });

  const canDispatch = t?.status === "Requested" && hasPermission(P.inventoryTransfer) && canAccessWarehouse(t.source.id);
  const canCancel = t?.status === "Requested" && hasPermission(P.inventoryTransfer) && canAccessWarehouse(t.source.id);
  const canReceive = t?.status === "InTransit" && hasPermission(P.inventoryReceive) && canAccessWarehouse(t.destination.id);
  const value = t?.lines.reduce((sum, l) => sum + l.quantity * l.unitCost, 0) ?? 0;

  return (
    <>
      <Drawer
        open={!!transferId}
        onClose={onClose}
        title={t ? `Transfer ${t.number}` : "Transfer"}
        subtitle={t ? <TransferStatusBadge status={t.status} /> : undefined}
        footer={
          t && (canDispatch || canReceive || canCancel) ? (
            <>
              {canCancel && (
                <Button variant="danger-ghost" icon={<Ban className="size-4" />} onClick={() => setPending("cancel")}>
                  Cancel transfer
                </Button>
              )}
              {canDispatch && (
                <Button icon={<Truck className="size-4" />} onClick={() => setPending("dispatch")}>
                  Dispatch
                </Button>
              )}
              {canReceive && (
                <Button icon={<CheckCircle2 className="size-4" />} onClick={() => setPending("receive")}>
                  Receive
                </Button>
              )}
            </>
          ) : undefined
        }
      >
        {query.isPending ? (
          <div className="space-y-4">
            <Skeleton className="h-16 w-full" />
            <Skeleton className="h-32 w-full" />
            <Skeleton className="h-40 w-full" />
          </div>
        ) : query.isError || !t ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        ) : (
          <div className="space-y-6">
            <div className="flex items-center gap-3 rounded-lg border border-slate-200 p-4">
              <div className="min-w-0 flex-1">
                <p className="text-xs uppercase tracking-wide text-slate-500">From</p>
                <p className="truncate font-medium text-slate-900">{t.source.name}</p>
                <p className="font-mono text-xs text-slate-500">{t.source.code}</p>
              </div>
              <ArrowRight className="size-5 shrink-0 text-slate-400" aria-hidden />
              <div className="min-w-0 flex-1 text-right">
                <p className="text-xs uppercase tracking-wide text-slate-500">To</p>
                <p className="truncate font-medium text-slate-900">{t.destination.name}</p>
                <p className="font-mono text-xs text-slate-500">{t.destination.code}</p>
              </div>
            </div>

            <section aria-labelledby="timeline-heading">
              <h3 id="timeline-heading" className="mb-3 text-sm font-semibold text-slate-900">
                Status
              </h3>
              <Timeline t={t} />
            </section>

            <section aria-labelledby="lines-heading">
              <h3 id="lines-heading" className="mb-2 text-sm font-semibold text-slate-900">
                Lines ({t.lines.length})
              </h3>
              <div className="overflow-hidden rounded-lg border border-slate-200">
                <Table>
                  <THead>
                    <tr>
                      <TH>Product</TH>
                      <TH align="right">Quantity</TH>
                      <TH align="right">Unit cost</TH>
                    </tr>
                  </THead>
                  <TBody>
                    {t.lines.map((l) => (
                      <TR key={l.productId}>
                        <TD>
                          <div className="font-medium text-slate-900">{l.productName}</div>
                          <div className="font-mono text-xs text-slate-500">{l.sku}</div>
                        </TD>
                        <TD numeric>{formatQty(l.quantity)}</TD>
                        <TD numeric>{l.unitCost ? formatCurrency(l.unitCost, currency, { precise: true }) : "—"}</TD>
                      </TR>
                    ))}
                  </TBody>
                </Table>
              </div>
              <dl className="mt-3 flex justify-end gap-6 text-sm">
                <div className="text-right">
                  <dt className="text-xs text-slate-500">Total units</dt>
                  <dd className="font-semibold tabular-nums text-slate-900">{formatQty(t.totalQuantity)}</dd>
                </div>
                {value > 0 && (
                  <div className="text-right">
                    <dt className="text-xs text-slate-500">Value at cost</dt>
                    <dd className="font-semibold tabular-nums text-slate-900">{formatCurrency(value, currency)}</dd>
                  </div>
                )}
              </dl>
            </section>

            {t.notes && (
              <section>
                <h3 className="mb-1 text-sm font-semibold text-slate-900">Notes</h3>
                <p className="whitespace-pre-wrap text-sm text-slate-600">{t.notes}</p>
              </section>
            )}
          </div>
        )}
      </Drawer>

      <ConfirmDialog
        open={!!pending && !!t}
        onClose={() => setPending(null)}
        title={pending && t ? `${ACTION_COPY[pending].title} ${t.number}?` : ""}
        description={pending && t ? ACTION_COPY[pending].body(t) : undefined}
        confirmLabel={pending ? ACTION_COPY[pending].confirm : "Confirm"}
        tone={pending === "cancel" ? "danger" : "primary"}
        onConfirm={() => (pending && t ? mutation.mutateAsync({ id: t.id, action: pending }) : undefined)}
      />
    </>
  );
}
