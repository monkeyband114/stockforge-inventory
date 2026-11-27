"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, CircleAlert } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Textarea } from "@/components/ui/Textarea";
import { useToast } from "@/components/ui/Toast";
import { roundMoney } from "@/features/sales/orderMath";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { formatCurrency, parseNumber } from "@/lib/format";
import { useIdempotentSubmission } from "@/lib/hooks";
import { P } from "@/lib/permissions";
import { qk, useCurrency } from "@/lib/queries";
import type { CreateSupplierInvoiceRequest, PagedResult, PurchaseOrder, PurchaseOrderSummary, SupplierInvoice } from "@/lib/types";
import { SupplierPicker, type PickedSupplier } from "./SupplierPicker";

const today = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
};

/** Bills can reference POs that have received goods (or are still open for delivery). */
const BILLABLE = new Set(["Approved", "PartiallyReceived", "Received", "Closed"]);

export function SupplierInvoiceDialog({
  open,
  onClose,
  presetSupplier,
  presetPurchaseOrderId,
}: {
  open: boolean;
  onClose: () => void;
  presetSupplier?: PickedSupplier | null;
  presetPurchaseOrderId?: string | null;
}) {
  const toast = useToast();
  const router = useRouter();
  const queryClient = useQueryClient();
  const { hasPermission } = useAuth();
  const currency = useCurrency();
  const submission = useIdempotentSubmission();
  const [supplier, setSupplier] = useState<PickedSupplier | null>(presetSupplier ?? null);
  const [poId, setPoId] = useState(presetPurchaseOrderId ?? "");
  const [number, setNumber] = useState("");
  const [invoiceDate, setInvoiceDate] = useState(today());
  const [dueDate, setDueDate] = useState("");
  const [subtotal, setSubtotal] = useState("");
  const [tax, setTax] = useState("");
  const [notes, setNotes] = useState("");
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setSupplier(presetSupplier ?? null);
    setPoId(presetPurchaseOrderId ?? "");
    setNumber("");
    setInvoiceDate(today());
    setDueDate("");
    setSubtotal("");
    setTax("");
    setNotes("");
    setErrors({});
    setFormError(null);
    submission.reset();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const canSeePos = hasPermission(P.purchasingView);
  const poParams = { supplierId: supplier?.id ?? "", pageSize: 50 };
  const pos = useQuery({
    queryKey: qk.purchaseOrders(poParams),
    queryFn: () => api.get<PagedResult<PurchaseOrderSummary>>("/api/purchase-orders", poParams),
    enabled: open && !!supplier && canSeePos,
  });
  const po = useQuery({
    queryKey: qk.purchaseOrder(poId),
    queryFn: () => api.get<PurchaseOrder>(`/api/purchase-orders/${poId}`),
    enabled: open && !!poId && canSeePos,
  });

  const sub = parseNumber(subtotal);
  const taxTotal = tax.trim() ? parseNumber(tax) : 0;
  const total = roundMoney((Number.isNaN(sub) ? 0 : sub) + (Number.isNaN(taxTotal) ? 0 : taxTotal));
  // Mirrors the server's three-way match: everything invoiced against the PO ≤ value received (+0.01 tolerance).
  const alreadyInvoiced = (po.data?.invoices ?? []).filter((i) => i.status !== "Cancelled").reduce((s, i) => s + i.total, 0);
  const matches = po.data ? alreadyInvoiced + total <= po.data.receivedValue + 0.01 : null;

  const mutation = useMutation({
    mutationFn: (body: CreateSupplierInvoiceRequest) => api.postIdempotent<SupplierInvoice>("/api/supplier-invoices", body, submission.keyFor(body)),
    onSuccess: (result) => {
      submission.reset();
      const s = result.data.summary;
      queryClient.setQueryData(qk.supplierInvoice(s.id), result.data);
      for (const root of ["supplier-invoices", "suppliers", "purchase-orders"]) void queryClient.invalidateQueries({ queryKey: [root] });
      if (result.replayed) toast.info(`${s.reference} already recorded`, "This submission was processed earlier — no duplicate bill was created.");
      else if (s.purchaseOrderId && !s.matchesReceipts) toast.info(`${s.reference} recorded — flagged for review`, "The bill exceeds the value of goods received on the purchase order.");
      else toast.success(`${s.reference} recorded`, s.purchaseOrderId ? "Matches the goods received." : undefined);
      onClose();
      router.push(`/payables/${s.id}`);
    },
    onError: (err) => {
      if (err instanceof ApiError && err.code === "duplicate_supplier_invoice") {
        setErrors({ supplierInvoiceNumber: err.detail });
        return;
      }
      const fe = fieldErrors(err);
      setErrors({ supplierId: fe.supplierId, supplierInvoiceNumber: fe.supplierInvoiceNumber, invoiceDate: fe.invoiceDate, dueDate: fe.dueDate, subtotal: fe.subtotal, taxTotal: fe.taxTotal, notes: fe.notes });
      setFormError(err instanceof Error ? err.message : "The bill could not be recorded.");
    },
  });

  const submit = () => {
    const money = (v: number, label: string) =>
      Number.isNaN(v) ? `${label} is required.` : v < 0 ? `${label} cannot be negative.` : Math.round(v * 100) / 100 !== v ? "At most 2 decimal places." : undefined;
    const e = {
      supplierId: supplier ? undefined : "Choose a supplier.",
      supplierInvoiceNumber: number.trim() ? (number.length > 64 ? "At most 64 characters." : undefined) : "The supplier's invoice number is required.",
      invoiceDate: invoiceDate ? undefined : "Invoice date is required.",
      dueDate: dueDate && invoiceDate && dueDate < invoiceDate ? "The due date cannot be before the invoice date." : undefined,
      subtotal: money(sub, "Subtotal"),
      taxTotal: tax.trim() ? money(taxTotal, "Tax") : undefined,
    };
    setErrors(e);
    setFormError(null);
    if (Object.values(e).some(Boolean)) return;
    mutation.mutate({
      supplierId: supplier!.id,
      purchaseOrderId: poId || null,
      supplierInvoiceNumber: number.trim(),
      invoiceDate,
      dueDate: dueDate || null,
      subtotal: sub,
      taxTotal,
      notes: notes.trim() || null,
    });
  };

  const poOptions = (pos.data?.items ?? []).filter((p) => BILLABLE.has(p.status) || p.id === poId).map((p) => ({ value: p.id, label: `${p.number} · ${formatCurrency(p.total, p.currency)}` }));

  return (
    <Dialog
      open={open}
      onClose={onClose}
      busy={mutation.isPending}
      size="lg"
      title="Record supplier invoice"
      description="Book a supplier's bill. Linking it to a purchase order checks it against the goods received (three-way match)."
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
          <Button type="submit" loading={mutation.isPending}>
            Record bill
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid gap-3 sm:grid-cols-2">
          <SupplierPicker
            value={supplier}
            onChange={(s) => {
              setSupplier(s);
              setPoId("");
            }}
            required
            error={errors.supplierId}
            disabled={!!presetSupplier}
          />
          <Select
            label="Purchase order"
            value={poId}
            onChange={(e) => setPoId(e.target.value)}
            disabled={!supplier || !canSeePos || !!presetPurchaseOrderId}
            placeholder={supplier ? "No purchase order" : "Choose a supplier first"}
            options={poOptions}
            hint={!canSeePos ? "You need purchasing access to link a purchase order." : undefined}
          />
          <Input label="Supplier invoice number" required value={number} onChange={(e) => setNumber(e.target.value)} error={errors.supplierInvoiceNumber} maxLength={64} />
          <div className="grid grid-cols-2 gap-3">
            <Input label="Invoice date" type="date" required value={invoiceDate} onChange={(e) => setInvoiceDate(e.target.value)} error={errors.invoiceDate} />
            <Input label="Due date" type="date" min={invoiceDate || undefined} value={dueDate} onChange={(e) => setDueDate(e.target.value)} error={errors.dueDate} hint="Blank = supplier terms" />
          </div>
          <Input label="Subtotal" required type="number" inputMode="decimal" min={0} step="0.01" trailing={currency} value={subtotal} onChange={(e) => setSubtotal(e.target.value)} error={errors.subtotal} />
          <Input label="Tax" type="number" inputMode="decimal" min={0} step="0.01" trailing={currency} value={tax} onChange={(e) => setTax(e.target.value)} error={errors.taxTotal} />
        </div>
        <div className="flex items-center justify-between rounded-md bg-slate-50 px-3 py-2 text-sm">
          <span className="text-slate-500">Bill total</span>
          <span className="font-semibold tabular-nums text-slate-900">{formatCurrency(total, currency)}</span>
        </div>
        {poId && po.data && matches !== null && (
          <div
            role="status"
            className={cn("flex gap-2 rounded-md border px-3 py-2 text-sm", matches ? "border-emerald-200 bg-emerald-50 text-emerald-800" : "border-amber-200 bg-amber-50 text-amber-900")}
          >
            {matches ? <CheckCircle2 className="mt-0.5 size-4 shrink-0" aria-hidden /> : <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />}
            <div>
              <p className="font-medium">{matches ? "Three-way match: within goods received" : "Three-way match: exceeds goods received"}</p>
              <p className="mt-0.5 text-xs">
                Received value {formatCurrency(po.data.receivedValue, currency)} · already invoiced {formatCurrency(alreadyInvoiced, currency)} · this bill {formatCurrency(total, currency)}.
                {!matches && " The bill can still be recorded; it will be flagged for review."}
              </p>
            </div>
          </div>
        )}
        <Textarea label="Notes" value={notes} onChange={(e) => setNotes(e.target.value)} error={errors.notes} maxLength={1000} />
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}
