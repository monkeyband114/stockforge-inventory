"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Checkbox } from "@/components/ui/Checkbox";
import { Dialog } from "@/components/ui/Dialog";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { useToast } from "@/components/ui/Toast";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { humanize } from "@/lib/format";
import { WAREHOUSE_TYPES, type Address, type CreateWarehouseRequest, type UpdateWarehouseRequest, type Warehouse, type WarehouseType } from "@/lib/types";
import { emailError, hasErrors } from "@/lib/validation";

const TYPE_LABELS: Record<WarehouseType, string> = {
  DistributionCenter: "Distribution center",
  Warehouse: "Warehouse",
  RetailStore: "Retail store",
  ThirdPartyLogistics: "Third-party logistics (3PL)",
};

export function warehouseTypeLabel(type: WarehouseType): string {
  return TYPE_LABELS[type] ?? humanize(type);
}

interface FormState {
  code: string;
  name: string;
  type: WarehouseType;
  line1: string;
  line2: string;
  city: string;
  state: string;
  postalCode: string;
  country: string;
  phone: string;
  email: string;
  isActive: boolean;
}

function toForm(w?: Warehouse | null): FormState {
  return {
    code: w?.code ?? "",
    name: w?.name ?? "",
    type: w?.type ?? "Warehouse",
    line1: w?.address?.line1 ?? "",
    line2: w?.address?.line2 ?? "",
    city: w?.address?.city ?? "",
    state: w?.address?.state ?? "",
    postalCode: w?.address?.postalCode ?? "",
    country: w?.address?.country ?? "",
    phone: w?.phone ?? "",
    email: w?.email ?? "",
    isActive: w?.isActive ?? true,
  };
}

const nullable = (v: string) => (v.trim() ? v.trim() : null);

export function WarehouseFormDialog({ open, onClose, warehouse }: { open: boolean; onClose: () => void; warehouse?: Warehouse | null }) {
  const editing = !!warehouse;
  const queryClient = useQueryClient();
  const toast = useToast();
  const [form, setForm] = useState<FormState>(toForm(warehouse));
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);

  useEffect(() => {
    if (open) {
      setForm(toForm(warehouse));
      setErrors({});
      setFormError(null);
      setConflict(false);
    }
  }, [open, warehouse]);

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => setForm((f) => ({ ...f, [key]: value }));

  const mutation = useMutation({
    mutationFn: async (): Promise<Warehouse> => {
      const anyAddress = [form.line1, form.line2, form.city, form.state, form.postalCode, form.country].some((v) => v.trim());
      const address: Address | null = anyAddress
        ? {
            line1: form.line1.trim(),
            line2: nullable(form.line2),
            city: form.city.trim(),
            state: nullable(form.state),
            postalCode: nullable(form.postalCode),
            country: form.country.trim(),
          }
        : null;
      if (editing && warehouse) {
        const body: UpdateWarehouseRequest = {
          name: form.name.trim(),
          type: form.type,
          address,
          phone: nullable(form.phone),
          email: nullable(form.email),
          isActive: form.isActive,
          version: warehouse.version,
        };
        return api.put<Warehouse>(`/api/warehouses/${warehouse.id}`, body);
      }
      const body: CreateWarehouseRequest = {
        code: form.code.trim().toUpperCase(),
        name: form.name.trim(),
        type: form.type,
        address,
        phone: nullable(form.phone),
        email: nullable(form.email),
      };
      return api.post<Warehouse>("/api/warehouses", body);
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ["warehouses"] });
      void queryClient.invalidateQueries({ queryKey: ["dashboard"] });
      toast.success(editing ? `Warehouse ${saved.code} updated` : `Warehouse ${saved.code} created`);
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
        line1: fe["address.line1"],
        city: fe["address.city"],
        country: fe["address.country"],
      });
      setFormError(err instanceof Error ? err.message : "Save failed.");
      toast.error(err);
    },
  });

  const submit = () => {
    const anyAddress = [form.line1, form.line2, form.city, form.state, form.postalCode, form.country].some((v) => v.trim());
    const next: Record<string, string | undefined> = {
      code: editing ? undefined : !form.code.trim() ? "Code is required." : !/^[A-Za-z0-9-]{1,20}$/.test(form.code.trim()) ? "Use up to 20 letters, digits or dashes." : undefined,
      name: form.name.trim() ? (form.name.length > 120 ? "At most 120 characters." : undefined) : "Name is required.",
      email: form.email.trim() ? emailError(form.email) : undefined,
      line1: anyAddress && !form.line1.trim() ? "Address line 1 is required when an address is given." : undefined,
      city: anyAddress && !form.city.trim() ? "City is required." : undefined,
      country: anyAddress && !form.country.trim() ? "Country is required." : undefined,
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
      title={editing ? `Edit ${warehouse?.code}` : "New warehouse"}
      description={editing ? "Update the site's details. The code cannot be changed." : "Add a stocking location: distribution center, warehouse, store or 3PL."}
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
          <Button type="submit" loading={mutation.isPending}>
            {editing ? "Save changes" : "Create warehouse"}
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        {conflict && (
          <div role="alert" className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
            Someone else changed this warehouse while you were editing. Close this dialog and reopen it to load the latest version.
          </div>
        )}
        <div className="grid gap-4 sm:grid-cols-3">
          <Input
            label="Code"
            value={form.code}
            onChange={(e) => set("code", e.target.value.toUpperCase())}
            error={errors.code}
            required={!editing}
            disabled={editing}
            maxLength={20}
            placeholder="e.g. CDC"
            className="font-mono uppercase"
          />
          <Input label="Name" value={form.name} onChange={(e) => set("name", e.target.value)} error={errors.name} required containerClassName="sm:col-span-2" maxLength={120} />
        </div>
        <Select
          label="Type"
          value={form.type}
          onChange={(e) => set("type", e.target.value as WarehouseType)}
          options={WAREHOUSE_TYPES.map((t) => ({ value: t, label: warehouseTypeLabel(t) }))}
        />
        <fieldset className="space-y-3">
          <legend className="text-sm font-semibold text-slate-900">Address</legend>
          <p className="-mt-2 text-xs text-slate-500">Optional. If provided, line 1, city and country are required.</p>
          <Input label="Address line 1" value={form.line1} onChange={(e) => set("line1", e.target.value)} error={errors.line1} maxLength={200} />
          <Input label="Address line 2" value={form.line2} onChange={(e) => set("line2", e.target.value)} maxLength={200} />
          <div className="grid gap-3 sm:grid-cols-2">
            <Input label="City" value={form.city} onChange={(e) => set("city", e.target.value)} error={errors.city} maxLength={100} />
            <Input label="State / region" value={form.state} onChange={(e) => set("state", e.target.value)} maxLength={100} />
            <Input label="Postal code" value={form.postalCode} onChange={(e) => set("postalCode", e.target.value)} maxLength={20} />
            <Input label="Country" value={form.country} onChange={(e) => set("country", e.target.value)} error={errors.country} maxLength={100} />
          </div>
        </fieldset>
        <div className="grid gap-3 sm:grid-cols-2">
          <Input label="Phone" type="tel" value={form.phone} onChange={(e) => set("phone", e.target.value)} error={errors.phone} maxLength={40} />
          <Input label="Email" type="email" value={form.email} onChange={(e) => set("email", e.target.value)} error={errors.email} maxLength={256} />
        </div>
        {editing && (
          <Checkbox
            label="Active"
            description="Inactive warehouses cannot receive or issue stock. A warehouse with stock on hand cannot be deactivated."
            checked={form.isActive}
            onChange={(e) => set("isActive", e.target.checked)}
          />
        )}
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}
