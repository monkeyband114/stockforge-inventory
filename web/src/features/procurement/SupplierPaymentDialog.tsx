"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { useToast } from "@/components/ui/Toast";
import { PAYMENT_METHOD_LABELS } from "@/features/sales/SalesBadges";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { formatCurrency, localDateTimeToIso, parseNumber } from "@/lib/format";
import { useIdempotentSubmission } from "@/lib/hooks";
import { qk } from "@/lib/queries";
import { PAYMENT_METHODS, type PaymentMethod, type RecordPaymentRequest, type SupplierInvoice } from "@/lib/types";

function nowLocal(): string {
  const d = new Date();
  d.setMinutes(d.getMinutes() - d.getTimezoneOffset());
  return d.toISOString().slice(0, 16);
}

/** Pays (part of) a supplier bill. Idempotency-Key required; amount ≤ balance due. */
export function SupplierPaymentDialog({ bill, open, onClose }: { bill: SupplierInvoice; open: boolean; onClose: () => void }) {
  const s = bill.summary;
  const toast = useToast();
  const queryClient = useQueryClient();
  const submission = useIdempotentSubmission();
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState<PaymentMethod>("BankTransfer");
  const [reference, setReference] = useState("");
  const [paidAt, setPaidAt] = useState("");
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setAmount(s.balanceDue > 0 ? String(s.balanceDue) : "");
      setMethod("BankTransfer");
      setReference("");
      setPaidAt(nowLocal());
      setErrors({});
      setFormError(null);
      submission.reset();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const mutation = useMutation({
    mutationFn: (body: RecordPaymentRequest) => api.postIdempotent<SupplierInvoice>(`/api/supplier-invoices/${s.id}/payments`, body, submission.keyFor(body)),
    onSuccess: (result) => {
      submission.reset();
      queryClient.setQueryData(qk.supplierInvoice(s.id), result.data);
      for (const root of ["supplier-invoices", "suppliers", "purchase-orders"]) void queryClient.invalidateQueries({ queryKey: [root] });
      if (result.replayed) toast.info("Payment already recorded", "This submission was processed earlier — the supplier was not paid twice.");
      else
        toast.success(
          `Payment recorded on ${s.reference}`,
          result.data.summary.balanceDue > 0 ? `Remaining ${formatCurrency(result.data.summary.balanceDue, s.currency)}.` : "The bill is now paid in full.",
        );
      onClose();
    },
    onError: (err) => {
      if (err instanceof ApiError && (err.code === "overpayment" || err.code === "invoice_paid" || err.code === "invalid_state")) {
        setErrors({ amount: err.detail });
        void queryClient.invalidateQueries({ queryKey: qk.supplierInvoice(s.id) });
        return;
      }
      const fe = fieldErrors(err);
      setErrors({ amount: fe.amount, method: fe.method, reference: fe.reference, paidAt: fe.paidAt });
      setFormError(err instanceof Error ? err.message : "Could not record the payment.");
    },
  });

  const submit = () => {
    const value = parseNumber(amount);
    const iso = paidAt ? localDateTimeToIso(paidAt) : null;
    const e = {
      amount: Number.isNaN(value)
        ? "Amount is required."
        : value <= 0
          ? "Amount must be greater than zero."
          : Math.round(value * 100) / 100 !== value
            ? "At most 2 decimal places."
            : value > s.balanceDue
              ? `Cannot exceed the balance due of ${formatCurrency(s.balanceDue, s.currency)}.`
              : undefined,
      reference: reference.length > 100 ? "At most 100 characters." : undefined,
      paidAt: iso && new Date(iso).getTime() > Date.now() + 5 * 60_000 ? "Payment date cannot be in the future." : undefined,
    };
    setErrors(e);
    setFormError(null);
    if (Object.values(e).some(Boolean)) return;
    mutation.mutate({ amount: value, method, reference: reference.trim() || null, paidAt: iso });
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      busy={mutation.isPending}
      title={`Pay ${s.reference}`}
      description={`${s.supplierName} · supplier invoice ${s.supplierInvoiceNumber} · balance ${formatCurrency(s.balanceDue, s.currency)}`}
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
          <Button type="submit" loading={mutation.isPending}>
            Record payment
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid gap-3 sm:grid-cols-2">
          <Input label="Amount" required type="number" inputMode="decimal" min={0} max={s.balanceDue} step="0.01" trailing={s.currency} value={amount} onChange={(e) => setAmount(e.target.value)} error={errors.amount} data-autofocus />
          <Select label="Method" value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)} error={errors.method} options={PAYMENT_METHODS.map((m) => ({ value: m, label: PAYMENT_METHOD_LABELS[m] }))} />
        </div>
        <Input label="Reference" value={reference} onChange={(e) => setReference(e.target.value)} error={errors.reference} maxLength={100} placeholder="e.g. remittance or transfer ID" />
        <Input label="Paid at" type="datetime-local" value={paidAt} max={nowLocal()} onChange={(e) => setPaidAt(e.target.value)} error={errors.paidAt} />
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}
