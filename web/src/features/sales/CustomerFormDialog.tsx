"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Checkbox } from "@/components/ui/Checkbox";
import { Dialog } from "@/components/ui/Dialog";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Tabs } from "@/components/ui/Tabs";
import { Textarea } from "@/components/ui/Textarea";
import { useToast } from "@/components/ui/Toast";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { parseNumber } from "@/lib/format";
import type { Customer, CustomerRequest } from "@/lib/types";
import { emailError, hasErrors } from "@/lib/validation";
import { AddressFields, addressErrors, addressToForm, formToAddress, type AddressForm } from "./AddressFields";

interface FormState {
  name: string;
  email: string;
  phone: string;
  taxNumber: string;
  paymentTermsDays: string;
  creditLimit: string;
  notes: string;
  isActive: boolean;
  billing: AddressForm;
  shipping: AddressForm;
  shippingSameAsBilling: boolean;
}

function toForm(c?: Customer | null): FormState {
  const billing = addressToForm(c?.billingAddress);
  const shipping = addressToForm(c?.shippingAddress);
  return {
    name: c?.name ?? "",
    email: c?.email ?? "",
    phone: c?.phone ?? "",
    taxNumber: c?.taxNumber ?? "",
    paymentTermsDays: c?.paymentTermsDays != null ? String(c.paymentTermsDays) : "",
    creditLimit: c?.creditLimit != null ? String(c.creditLimit) : "",
    notes: c?.notes ?? "",
    isActive: c?.isActive ?? true,
    billing,
    shipping,
    shippingSameAsBilling: !c || JSON.stringify(billing) === JSON.stringify(shipping),
  };
}

const nullable = (v: string) => (v.trim() ? v.trim() : null);

export function CustomerFormDialog({
  open,
  onClose,
  customer,
  onSaved,
}: {
  open: boolean;
  onClose: () => void;
  customer?: Customer | null;
  onSaved?: (c: Customer) => void;
}) {
  const editing = !!customer;
  const toast = useToast();
  const queryClient = useQueryClient();
  const [form, setForm] = useState<FormState>(toForm(customer));
  const [tab, setTab] = useState<"billing" | "shipping">("billing");
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);

  useEffect(() => {
    if (open) {
      setForm(toForm(customer));
      setTab("billing");
      setErrors({});
      setFormError(null);
      setConflict(false);
    }
  }, [open, customer]);

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => setForm((f) => ({ ...f, [key]: value }));

  const mutation = useMutation({
    mutationFn: async () => {
      const billing = formToAddress(form.billing);
      const body: CustomerRequest = {
        name: form.name.trim(),
        email: nullable(form.email),
        phone: nullable(form.phone),
        taxNumber: nullable(form.taxNumber),
        billingAddress: billing,
        shippingAddress: form.shippingSameAsBilling ? billing : formToAddress(form.shipping),
        paymentTermsDays: form.paymentTermsDays.trim() ? parseNumber(form.paymentTermsDays) : null,
        creditLimit: form.creditLimit.trim() ? parseNumber(form.creditLimit) : null,
        notes: nullable(form.notes),
        isActive: form.isActive,
        version: customer?.version ?? null,
      };
      return editing ? api.put<Customer>(`/api/customers/${customer!.id}`, body) : api.post<Customer>("/api/customers", body);
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ["customers"] });
      toast.success(editing ? `${saved.name} updated` : `Customer ${saved.code} created`);
      onSaved?.(saved);
      onClose();
    },
    onError: (err) => {
      if (err instanceof ApiError && err.code === "concurrency_conflict") {
        setConflict(true);
        return;
      }
      const fe = fieldErrors(err);
      setErrors({
        ...fe,
        "billing.line1": fe["billingAddress.line1"],
        "billing.city": fe["billingAddress.city"],
        "billing.country": fe["billingAddress.country"],
        "shipping.line1": fe["shippingAddress.line1"],
        "shipping.city": fe["shippingAddress.city"],
        "shipping.country": fe["shippingAddress.country"],
      });
      setFormError(err instanceof Error ? err.message : "Save failed.");
      if (!(err instanceof ApiError && err.status === 400)) toast.error(err);
    },
  });

  const submit = () => {
    const terms = form.paymentTermsDays.trim() ? parseNumber(form.paymentTermsDays) : null;
    const limit = form.creditLimit.trim() ? parseNumber(form.creditLimit) : null;
    const be = addressErrors(form.billing);
    const se = form.shippingSameAsBilling ? {} : addressErrors(form.shipping);
    const next: Record<string, string | undefined> = {
      name: form.name.trim() ? (form.name.length > 200 ? "At most 200 characters." : undefined) : "Name is required.",
      email: form.email.trim() ? emailError(form.email) : undefined,
      paymentTermsDays: terms !== null && (!Number.isInteger(terms) || terms < 0 || terms > 365) ? "Whole days between 0 and 365." : undefined,
      creditLimit: limit !== null && (Number.isNaN(limit) || limit < 0) ? "Credit limit must be zero or more." : undefined,
      "billing.line1": be.line1,
      "billing.city": be.city,
      "billing.country": be.country,
      "shipping.line1": se.line1,
      "shipping.city": se.city,
      "shipping.country": se.country,
    };
    setErrors(next);
    setFormError(null);
    if (hasErrors(next)) {
      if (next["billing.line1"] || next["billing.city"] || next["billing.country"]) setTab("billing");
      else if (next["shipping.line1"] || next["shipping.city"] || next["shipping.country"]) setTab("shipping");
      return;
    }
    mutation.mutate();
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      busy={mutation.isPending}
      size="lg"
      title={editing ? `Edit ${customer?.name}` : "New customer"}
      description={editing ? `Customer ${customer?.code}` : "The customer code is assigned automatically."}
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
          <Button type="submit" loading={mutation.isPending}>
            {editing ? "Save changes" : "Create customer"}
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        {conflict && (
          <div role="alert" className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
            Someone else changed this customer while you were editing. Close the dialog and reopen it to load the latest version.
          </div>
        )}
        <Input label="Name" required value={form.name} onChange={(e) => set("name", e.target.value)} error={errors.name} maxLength={200} />
        <div className="grid gap-3 sm:grid-cols-3">
          <Input label="Email" type="email" value={form.email} onChange={(e) => set("email", e.target.value)} error={errors.email} maxLength={256} hint="Invoices and confirmations go here." />
          <Input label="Phone" type="tel" value={form.phone} onChange={(e) => set("phone", e.target.value)} error={errors.phone} maxLength={40} />
          <Input label="Tax number" value={form.taxNumber} onChange={(e) => set("taxNumber", e.target.value)} error={errors.taxNumber} maxLength={64} />
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
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
            hint="Blank uses the organization's invoice due days."
          />
          <Input
            label="Credit limit"
            type="number"
            min={0}
            step="any"
            value={form.creditLimit}
            onChange={(e) => set("creditLimit", e.target.value)}
            error={errors.creditLimit}
            hint="Orders that would exceed it need approval. Blank = no limit."
          />
        </div>

        <div>
          <Tabs
            tabs={[
              { value: "billing" as const, label: "Billing address" },
              { value: "shipping" as const, label: "Shipping address" },
            ]}
            value={tab}
            onChange={setTab}
          />
          <div className="pt-4">
            {tab === "billing" ? (
              <AddressFields
                value={form.billing}
                onChange={(v) => set("billing", v)}
                errors={{ line1: errors["billing.line1"], city: errors["billing.city"], country: errors["billing.country"] }}
              />
            ) : (
              <div className="space-y-3">
                <Checkbox label="Same as billing address" checked={form.shippingSameAsBilling} onChange={(e) => set("shippingSameAsBilling", e.target.checked)} />
                {!form.shippingSameAsBilling && (
                  <AddressFields
                    value={form.shipping}
                    onChange={(v) => set("shipping", v)}
                    errors={{ line1: errors["shipping.line1"], city: errors["shipping.city"], country: errors["shipping.country"] }}
                  />
                )}
              </div>
            )}
          </div>
        </div>

        <Textarea label="Notes" value={form.notes} onChange={(e) => set("notes", e.target.value)} maxLength={2000} />
        {editing && (
          <Checkbox
            label="Active"
            description="Inactive customers keep their history but cannot receive new orders."
            checked={form.isActive}
            onChange={(e) => set("isActive", e.target.checked)}
          />
        )}
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}
