"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { controlClasses, FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Spinner } from "@/components/ui/Spinner";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { Textarea } from "@/components/ui/Textarea";
import { useToast } from "@/components/ui/Toast";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { cn } from "@/lib/cn";
import { formatQty, parseNumber } from "@/lib/format";
import { useIdempotentSubmission } from "@/lib/hooks";
import { qk } from "@/lib/queries";
import type { PurchaseOrder, ReceiveGoodsRequest } from "@/lib/types";

/** Every query family a goods receipt changes (stock-in movements, PO progress, supplier counts, dashboards). */
export const RECEIPT_ROOTS = ["purchase-orders", "inventory", "movements", "dashboard", "products", "suppliers", "supplier-invoices"];

/**
 * Goods received note for one delivery: per-line quantities default to what is outstanding. The POST requires
 * an Idempotency-Key, keyed per (order version, payload) so a retry never books the same delivery twice.
 */
export function ReceiveGoodsDialog({ purchaseOrderId, open, onClose }: { purchaseOrderId: string | null; open: boolean; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const submission = useIdempotentSubmission();
  const po = useQuery({
    queryKey: qk.purchaseOrder(purchaseOrderId ?? ""),
    queryFn: () => api.get<PurchaseOrder>(`/api/purchase-orders/${purchaseOrderId}`),
    enabled: open && !!purchaseOrderId,
  });
  const order = po.data;
  const [qty, setQty] = useState<Record<string, string>>({});
  const [deliveryNote, setDeliveryNote] = useState("");
  const [notes, setNotes] = useState("");
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);

  useEffect(() => {
    if (open && order) {
      setQty(Object.fromEntries(order.lines.map((l) => [l.id, l.quantityOutstanding > 0 ? String(l.quantityOutstanding) : ""])));
      setDeliveryNote("");
      setNotes("");
      setErrors({});
      setFormError(null);
      submission.reset();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, order?.id, order?.version]);

  const mutation = useMutation({
    mutationFn: (body: ReceiveGoodsRequest) =>
      api.postIdempotent<PurchaseOrder>(`/api/purchase-orders/${order!.id}/receipts`, body, submission.keyFor({ v: order!.version, body })),
    onSuccess: (result) => {
      submission.reset();
      queryClient.setQueryData(qk.purchaseOrder(result.data.id), result.data);
      for (const root of RECEIPT_ROOTS) void queryClient.invalidateQueries({ queryKey: [root], refetchType: "active" });
      const grn = [...result.data.receipts].sort((a, b) => b.receivedAt.localeCompare(a.receivedAt))[0];
      if (result.replayed) toast.info("Delivery already booked", "This receipt was recorded earlier — stock was not added twice.");
      else toast.success(`Goods received${grn ? ` — ${grn.number}` : ""}`, result.data.status === "Received" ? `${result.data.number} is fully received.` : `${result.data.number} is partially received.`);
      onClose();
    },
    onError: (err) => {
      if (err instanceof ApiError && err.code === "over_receipt") {
        setFormError(err.detail);
        void po.refetch();
        return;
      }
      const fe = fieldErrors(err);
      setErrors({ deliveryNote: fe.deliveryNote, notes: fe.notes });
      setFormError(err instanceof Error ? err.message : "The receipt could not be recorded.");
    },
  });

  const submit = () => {
    if (!order) return;
    const e: Record<string, string | undefined> = {};
    const lines: ReceiveGoodsRequest["lines"] = [];
    for (const l of order.lines) {
      const raw = qty[l.id]?.trim() ?? "";
      if (!raw) continue;
      const q = parseNumber(raw);
      if (Number.isNaN(q) || q < 0) e[l.id] = "Invalid quantity.";
      else if (q > l.quantityOutstanding) e[l.id] = `Only ${formatQty(l.quantityOutstanding)} outstanding.`;
      else if (q > 0) lines.push({ lineId: l.id, quantity: q });
    }
    if (deliveryNote.length > 64) e.deliveryNote = "At most 64 characters.";
    setErrors(e);
    if (Object.values(e).some(Boolean)) return;
    if (lines.length === 0) {
      setFormError("Enter a received quantity for at least one line.");
      return;
    }
    setFormError(null);
    mutation.mutate({ lines, deliveryNote: deliveryNote.trim() || null, notes: notes.trim() || null });
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      busy={mutation.isPending}
      size="lg"
      title={order ? `Receive goods — ${order.number}` : "Receive goods"}
      description={order ? `From ${order.supplierName} into ${order.warehouseCode}. Received units are booked as stock in at the PO cost.` : undefined}
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
          <Button type="submit" loading={mutation.isPending} disabled={!order}>
            Book receipt
          </Button>
        </>
      }
    >
      {po.isPending ? (
        <div className="flex items-center gap-2 py-8 text-sm text-slate-500">
          <Spinner /> Loading purchase order…
        </div>
      ) : po.isError || !order ? (
        <FormError message={po.error instanceof Error ? po.error.message : "The purchase order could not be loaded."} />
      ) : (
        <div className="space-y-4">
          <div className="overflow-hidden rounded-lg border border-slate-200">
            <Table>
              <THead>
                <tr>
                  <TH>Product</TH>
                  <TH align="right">Ordered</TH>
                  <TH align="right">Received</TH>
                  <TH align="right">Outstanding</TH>
                  <TH align="right">Receive now</TH>
                </tr>
              </THead>
              <TBody>
                {order.lines.map((l) => (
                  <TR key={l.id}>
                    <TD>
                      <div className="font-medium text-slate-900">{l.productName}</div>
                      <div className="font-mono text-xs text-slate-500">{l.sku}</div>
                    </TD>
                    <TD numeric>{formatQty(l.quantity)}</TD>
                    <TD numeric>{formatQty(l.quantityReceived)}</TD>
                    <TD numeric className={l.quantityOutstanding > 0 ? "font-medium text-slate-900" : "text-slate-400"}>
                      {formatQty(l.quantityOutstanding)}
                    </TD>
                    <TD align="right">
                      <input
                        type="number"
                        inputMode="decimal"
                        min={0}
                        max={l.quantityOutstanding}
                        step="any"
                        aria-label={`Quantity received for ${l.productName}`}
                        aria-invalid={errors[l.id] ? true : undefined}
                        disabled={l.quantityOutstanding <= 0}
                        value={qty[l.id] ?? ""}
                        onChange={(e) => setQty((q) => ({ ...q, [l.id]: e.target.value }))}
                        className={cn(controlClasses, "h-8 w-24 text-right tabular-nums")}
                      />
                      {errors[l.id] && <p className="mt-1 text-xs font-medium text-red-600">{errors[l.id]}</p>}
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          </div>
          <Input label="Supplier delivery note" value={deliveryNote} onChange={(e) => setDeliveryNote(e.target.value)} error={errors.deliveryNote} maxLength={64} placeholder="e.g. DN-1042" />
          <Textarea label="Notes" value={notes} onChange={(e) => setNotes(e.target.value)} error={errors.notes} maxLength={1000} placeholder="Damages, short shipments, back-orders…" />
          <FormError message={formError} />
        </div>
      )}
    </Dialog>
  );
}
