"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Checkbox } from "@/components/ui/Checkbox";
import { Dialog } from "@/components/ui/Dialog";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Textarea } from "@/components/ui/Textarea";
import { useToast } from "@/components/ui/Toast";
import { AddressFields, addressErrors, addressToForm, formToAddress, type AddressForm } from "@/features/sales/AddressFields";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { parseNumber } from "@/lib/format";
import type { Supplier, SupplierRequest } from "@/lib/types";
import { emailError, hasErrors } from "@/lib/validation";

interface FormState {
  name: string;
  contactName: string;
  email: string;
  phone: string;
  taxNumber: string;
  paymentTermsDays: string;
  leadTimeDays: string;
  notes: string;
  isActive: boolean;
  address: AddressForm;
}

const toForm = (s?: Supplier | null): FormState => ({
  name: s?.name ?? "",
  contactName: s?.contactName ?? "",
  email: s?.email ?? "",
  phone: s?.phone ?? "",
  taxNumber: s?.taxNumber ?? "",
  paymentTermsDays: s?.paymentTermsDays != null ? String(s.paymentTermsDays) : "",
  leadTimeDays: s?.leadTimeDays != null ? String(s.leadTimeDays) : "",
  notes: s?.notes ?? "",
  isActive: s?.isActive ?? true,
  address: addressToForm(s?.address),
});

const nullable = (v: string) => (v.trim() ? v.trim() : null);
const days = (v: string) => (v.trim() ? parseNumber(v) : null);
const daysError = (v: number | null) => (v !== null && (!Number.isInteger(v) || v < 0 || v > 365) ? "Whole days between 0 and 365." : undefined);

export function SupplierFormDialog({ open, onClose, supplier }: { open: boolean; onClose: () => void; supplier?: Supplier | null }) {
  const editing = !!supplier;
  const toast = useToast();
  const queryClient = useQueryClient();
  const [form, setForm] = useState<FormState>(toForm(supplier));
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);

  useEffect(() => {
    if (open) {
      setForm(toForm(supplier));
      setErrors({});
      setFormError(null);
      setConflict(false);
    }
  }, [open, supplier]);

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => setForm((f) => ({ ...f, [key]: value }));

  const mutation = useMutation({
    mutationFn: () => {
      const body: SupplierRequest = {
        name: form.name.trim(),
        contactName: nullable(form.contactName),
        email: nullable(form.email),
        phone: nullable(form.phone),
        taxNumber: nullable(form.taxNumber),
        address: formToAddress(form.address),
        paymentTermsDays: days(form.paymentTermsDays),
        leadTimeDays: days(form.leadTimeDays),
        notes: nullable(form.notes),
        isActive: form.isActive,
        version: supplier?.version ?? null,
      };
      return editing ? api.put<Supplier>(`/api/suppliers/${supplier!.id}`, body) : api.post<Supplier>("/api/suppliers", body);
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ["suppliers"] });
      toast.success(editing ? `${saved.name} updated` : `Supplier ${saved.code} created`);
      onClose();
    },
    onError: (err) => {
      if (err instanceof ApiError && err.code === "concurrency_conflict") {
        setConflict(true);
        return;
      }
      const fe = fieldErrors(err);
      setErrors({ ...fe, line1: fe["address.line1"], city: fe["address.city"], country: fe["address.country"] });
      setFormError(err instanceof Error ? err.message : "Save failed.");
      if (!(err instanceof ApiError && err.status === 400)) toast.error(err);
    },
  });

  const submit = () => {
    const ae = addressErrors(form.address);
    const next: Record<string, string | undefined> = {
      name: form.name.trim() ? (form.name.length > 200 ? "At most 200 characters." : undefined) : "Name is required.",
      email: form.email.trim() ? emailError(form.email) : undefined,
      paymentTermsDays: daysError(days(form.paymentTermsDays)),
      leadTimeDays: daysError(days(form.leadTimeDays)),
      ...ae,
    };
    setErrors(next);
    setFormError(null);
    if (hasErrors(next)) return;
    mutation.mutate();
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      busy={mutation.isPending}
      size="lg"
      title={editing ? `Edit ${supplier?.name}` : "New supplier"}
      description={editing ? `Supplier ${supplier?.code}` : "The supplier code is assigned automatically."}
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
          <Button type="submit" loading={mutation.isPending}>
            {editing ? "Save changes" : "Create supplier"}
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        {conflict && (
          <div role="alert" className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
            Someone else changed this supplier while you were editing. Close the dialog and reopen it to load the latest version.
          </div>
        )}
        <Input label="Name" required value={form.name} onChange={(e) => set("name", e.target.value)} error={errors.name} maxLength={200} />
        <div className="grid gap-3 sm:grid-cols-2">
          <Input label="Contact person" value={form.contactName} onChange={(e) => set("contactName", e.target.value)} error={errors.contactName} maxLength={120} />
          <Input label="Email" type="email" value={form.email} onChange={(e) => set("email", e.target.value)} error={errors.email} maxLength={256} />
          <Input label="Phone" type="tel" value={form.phone} onChange={(e) => set("phone", e.target.value)} error={errors.phone} maxLength={40} />
          <Input label="Tax number" value={form.taxNumber} onChange={(e) => set("taxNumber", e.target.value)} error={errors.taxNumber} maxLength={64} />
          <Input
            label="Payment terms"
            type="number"
            min={0}
            max={365}
            step={1}
            trailing="days"
            value={form.paymentTermsDays}
            onChange={(e) => set("paymentTermsDays", e.target.value)}
            error={errors.paymentTermsDays}
            hint="Default due date for this supplier's bills."
          />
          <Input
            label="Lead time"
            type="number"
            min={0}
            max={365}
            step={1}
            trailing="days"
            value={form.leadTimeDays}
            onChange={(e) => set("leadTimeDays", e.target.value)}
            error={errors.leadTimeDays}
          />
        </div>
        <fieldset className="space-y-3">
          <legend className="text-sm font-semibold text-slate-900">Address</legend>
          <AddressFields value={form.address} onChange={(v) => set("address", v)} errors={{ line1: errors.line1, city: errors.city, country: errors.country }} />
        </fieldset>
        <Textarea label="Notes" value={form.notes} onChange={(e) => set("notes", e.target.value)} maxLength={2000} />
        {editing && (
          <Checkbox
            label="Active"
            description="Inactive suppliers keep their history but cannot receive new purchase orders."
            checked={form.isActive}
            onChange={(e) => set("isActive", e.target.checked)}
          />
        )}
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}
