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
import { PAYMENT_METHODS, type Invoice, type PaymentMethod, type RecordPaymentRequest } from "@/lib/types";

function nowLocal(): string {
  const d = new Date();
  d.setMinutes(d.getMinutes() - d.getTimezoneOffset());
  return d.toISOString().slice(0, 16);
}

export function RecordPaymentDialog({ invoice, open, onClose }: { invoice: Invoice; open: boolean; onClose: () => void }) {
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
      setAmount(invoice.balanceDue > 0 ? String(invoice.balanceDue) : "");
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
    mutationFn: async (body: RecordPaymentRequest) => {
      // Required by the API: one key per submission so a retry never records the payment twice.
      return api.postIdempotent<Invoice>(`/api/invoices/${invoice.id}/payments`, body, submission.keyFor(body));
    },
    onSuccess: (result) => {
      submission.reset();
      queryClient.setQueryData(qk.invoice(invoice.id), result.data);
      for (const root of ["invoices", "orders", "customers", "finance", "dashboard"]) void queryClient.invalidateQueries({ queryKey: [root] });
      if (result.replayed) toast.info("Payment already recorded", "This submission was processed earlier — no duplicate payment was made.");
      else
        toast.success(
          `Payment recorded on ${invoice.number}`,
          result.data.balanceDue > 0 ? `Remaining balance ${formatCurrency(result.data.balanceDue, invoice.currency)}.` : "The invoice is now paid in full.",
        );
      onClose();
    },
    onError: (err) => {
      if (err instanceof ApiError && (err.code === "overpayment" || err.code === "invoice_paid")) {
        setErrors({ amount: err.detail });
        void queryClient.invalidateQueries({ queryKey: qk.invoice(invoice.id) });
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
            : value > invoice.balanceDue
              ? `Cannot exceed the balance due of ${formatCurrency(invoice.balanceDue, invoice.currency)}.`
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
      title={`Record payment — ${invoice.number}`}
      description={`Balance due ${formatCurrency(invoice.balanceDue, invoice.currency)} from ${invoice.billingName}.`}
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
          <Input
            label="Amount"
            required
            type="number"
            inputMode="decimal"
            min={0}
            max={invoice.balanceDue}
            step="0.01"
            trailing={invoice.currency}
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            error={errors.amount}
            data-autofocus
          />
          <Select
            label="Method"
            value={method}
            onChange={(e) => setMethod(e.target.value as PaymentMethod)}
            error={errors.method}
            options={PAYMENT_METHODS.map((m) => ({ value: m, label: PAYMENT_METHOD_LABELS[m] }))}
          />
        </div>
        <Input label="Reference" value={reference} onChange={(e) => setReference(e.target.value)} error={errors.reference} maxLength={100} placeholder="e.g. bank transaction ID" />
        <Input label="Paid at" type="datetime-local" value={paidAt} max={nowLocal()} onChange={(e) => setPaidAt(e.target.value)} error={errors.paidAt} />
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}
