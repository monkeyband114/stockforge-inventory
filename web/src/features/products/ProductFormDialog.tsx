"use client";

import { useQueryClient } from "@tanstack/react-query";
import { Plus, RefreshCw, Trash2 } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Checkbox } from "@/components/ui/Checkbox";
import { Dialog } from "@/components/ui/Dialog";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Textarea } from "@/components/ui/Textarea";
import { useToast } from "@/components/ui/Toast";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { qk, useCategories, useUnits } from "@/lib/queries";
import type { Product } from "@/lib/types";
import {
  emptyProductForm,
  emptyUnitRow,
  productToForm,
  toCreateRequest,
  toUpdateRequest,
  validateProductForm,
  type FormErrors,
  type ProductFormState,
} from "./productForm";

export interface ProductFormDialogProps {
  open: boolean;
  onClose: () => void;
  mode: "create" | "edit";
  /** Required in edit mode. */
  product?: Product;
  /** Edit mode: re-fetch the product after a concurrency conflict. */
  onReload?: () => Promise<Product | undefined>;
  onSaved?: (product: Product) => void;
}

function SectionTitle({ children }: { children: React.ReactNode }) {
  return <h3 className="col-span-full mt-2 text-xs font-semibold uppercase tracking-wide text-slate-500 first:mt-0">{children}</h3>;
}

export function ProductFormDialog({ open, onClose, mode, product, onReload, onSaved }: ProductFormDialogProps) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const categories = useCategories();
  const units = useUnits();
  const [form, setForm] = useState<ProductFormState>(emptyProductForm);
  const [errors, setErrors] = useState<FormErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [saving, setSaving] = useState(false);
  const [reloading, setReloading] = useState(false);

  // Reset whenever the dialog opens.
  useEffect(() => {
    if (!open) return;
    setForm(mode === "edit" && product ? productToForm(product) : emptyProductForm());
    setErrors({});
    setFormError(null);
    setConflict(false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  // Default the base unit to "EA" (or the first active unit) for new products.
  useEffect(() => {
    if (!open || mode !== "create" || form.baseUnitId || !units.data) return;
    const active = units.data.filter((u) => u.isActive);
    const preferred = active.find((u) => u.code.toUpperCase() === "EA") ?? active[0];
    if (preferred) setForm((f) => ({ ...f, baseUnitId: preferred.id }));
  }, [open, mode, form.baseUnitId, units.data]);

  const set = <K extends keyof ProductFormState>(key: K, value: ProductFormState[K]) => setForm((f) => ({ ...f, [key]: value }));

  const categoryOptions = useMemo(() => {
    const list = (categories.data ?? []).filter((c) => c.isActive || c.id === form.categoryId);
    return list.map((c) => ({ value: c.id, label: c.isActive ? `${c.name} (${c.code})` : `${c.name} (inactive)` }));
  }, [categories.data, form.categoryId]);

  const baseUnit = units.data?.find((u) => u.id === form.baseUnitId);
  const baseUnitCode = baseUnit?.code ?? product?.baseUnitCode ?? "base";

  const unitOptionsFor = (rowIndex: number) => {
    const usedElsewhere = new Set(form.units.filter((_, i) => i !== rowIndex).map((u) => u.unitId));
    const current = form.units[rowIndex]?.unitId;
    return (units.data ?? [])
      .filter((u) => u.id !== form.baseUnitId && !usedElsewhere.has(u.id) && (u.isActive || u.id === current))
      .map((u) => ({ value: u.id, label: `${u.code} — ${u.name}${u.isActive ? "" : " (inactive)"}` }));
  };

  const availableForNewRow = (units.data ?? []).filter(
    (u) => u.isActive && u.id !== form.baseUnitId && !form.units.some((r) => r.unitId === u.id),
  ).length;

  const updateUnit = (index: number, patch: Partial<ProductFormState["units"][number]>) =>
    setForm((f) => ({ ...f, units: f.units.map((u, i) => (i === index ? { ...u, ...patch } : u)) }));

  const reload = async () => {
    if (!onReload) return;
    setReloading(true);
    try {
      const fresh = await onReload();
      if (fresh) setForm(productToForm(fresh));
      setConflict(false);
      setFormError(null);
      setErrors({});
    } finally {
      setReloading(false);
    }
  };

  const submit = async () => {
    const v = validateProductForm(form, mode);
    setErrors(v);
    if (Object.keys(v).length > 0) {
      setFormError("Please fix the highlighted fields.");
      return;
    }
    setSaving(true);
    setFormError(null);
    setConflict(false);
    try {
      let saved: Product;
      if (mode === "create") {
        saved = await api.post<Product>("/api/products", toCreateRequest(form));
        toast.success("Product created", `${saved.name} · ${saved.sku}`);
      } else {
        if (!product) return;
        saved = await api.put<Product>(`/api/products/${product.id}`, toUpdateRequest(form, product.version, product));
        toast.success("Product updated", saved.name);
      }
      queryClient.setQueryData(qk.product(saved.id), saved);
      void queryClient.invalidateQueries({ queryKey: ["products"] });
      void queryClient.invalidateQueries({ queryKey: qk.categories });
      onSaved?.(saved);
      onClose();
    } catch (err) {
      if (err instanceof ApiError && err.code === "concurrency_conflict") {
        setConflict(true);
      } else {
        setErrors(fieldErrors(err));
        setFormError(err instanceof ApiError ? err.detail : "Saving failed.");
        toast.error(err);
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      busy={saving}
      size="lg"
      title={mode === "create" ? "New product" : `Edit ${product?.name ?? "product"}`}
      description={mode === "create" ? "Add a product to the catalog. Stock is recorded separately per warehouse." : undefined}
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Cancel
          </Button>
          <Button type="submit" loading={saving} disabled={conflict}>
            {mode === "create" ? "Create product" : "Save changes"}
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        {conflict && (
          <div role="alert" className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-amber-200 bg-amber-50 px-3 py-2.5 text-sm text-amber-900">
            <span>Someone else changed this product while you were editing. Reload to get the latest version, then re-apply your changes.</span>
            <Button size="sm" variant="secondary" onClick={reload} loading={reloading} icon={<RefreshCw className="size-3.5" />}>
              Reload
            </Button>
          </div>
        )}

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <SectionTitle>Identification</SectionTitle>
          {mode === "create" ? (
            <Input
              label="SKU"
              value={form.sku}
              onChange={(e) => set("sku", e.target.value)}
              error={errors.sku}
              hint="Leave blank to auto-generate from the category code."
              placeholder="Auto-generated"
              maxLength={64}
              className="font-mono uppercase"
            />
          ) : (
            <Input label="SKU" value={form.sku} readOnly disabled hint="SKUs cannot be changed." className="font-mono" />
          )}
          <Input label="Name" value={form.name} onChange={(e) => set("name", e.target.value)} error={errors.name} required maxLength={200} data-autofocus />
          <Textarea
            label="Description"
            containerClassName="sm:col-span-2"
            value={form.description}
            onChange={(e) => set("description", e.target.value)}
            error={errors.description}
            maxLength={4000}
            rows={2}
          />

          <SectionTitle>Classification</SectionTitle>
          <Select
            label="Category"
            value={form.categoryId}
            onChange={(e) => set("categoryId", e.target.value)}
            error={errors.categoryId}
            placeholder="Uncategorized"
            options={categoryOptions}
          />
          {mode === "create" ? (
            <Select
              label="Base unit"
              value={form.baseUnitId}
              onChange={(e) => {
                const next = e.target.value;
                setForm((f) => ({ ...f, baseUnitId: next, units: f.units.filter((u) => u.unitId !== next) }));
              }}
              error={errors.baseUnitId}
              required
              placeholder={units.isPending ? "Loading units…" : "Select a unit"}
              hint="Stock is counted in this unit."
              options={(units.data ?? []).filter((u) => u.isActive).map((u) => ({ value: u.id, label: `${u.code} — ${u.name}` }))}
            />
          ) : (
            <Input label="Base unit" value={product?.baseUnitCode ?? ""} readOnly disabled hint="The base unit cannot be changed." />
          )}

          <SectionTitle>Barcode</SectionTitle>
          <Input
            label="Barcode"
            value={form.generateBarcode ? "" : form.barcode}
            onChange={(e) => set("barcode", e.target.value)}
            error={errors.barcode}
            disabled={form.generateBarcode}
            placeholder={form.generateBarcode ? "Will be generated (EAN-13)" : "e.g. 4006381333931"}
            maxLength={64}
            className="font-mono"
            hint="Letters, digits, '-' or '.'; 13-digit codes must have a valid EAN-13 check digit."
          />
          {mode === "create" && (
            <div className="flex items-end pb-2">
              <Checkbox
                label="Generate barcode"
                description="Assign an internal EAN-13 (GS1 in-store range)."
                checked={form.generateBarcode}
                onChange={(e) => setForm((f) => ({ ...f, generateBarcode: e.target.checked, barcode: e.target.checked ? "" : f.barcode }))}
              />
            </div>
          )}

          <SectionTitle>Pricing</SectionTitle>
          <Input
            label="Cost price"
            type="number"
            inputMode="decimal"
            min={0}
            step="0.0001"
            value={form.costPrice}
            onChange={(e) => set("costPrice", e.target.value)}
            error={errors.costPrice}
            required
          />
          <Input
            label="Selling price"
            type="number"
            inputMode="decimal"
            min={0}
            step="0.01"
            value={form.sellingPrice}
            onChange={(e) => set("sellingPrice", e.target.value)}
            error={errors.sellingPrice}
            required
          />
          <Input
            label="Tax rate"
            type="number"
            inputMode="decimal"
            min={0}
            max={100}
            step="0.001"
            trailing="%"
            value={form.taxRatePercent}
            onChange={(e) => set("taxRatePercent", e.target.value)}
            error={errors.taxRate}
            hint="Leave blank to use the organization default."
          />

          <SectionTitle>Replenishment</SectionTitle>
          <Input
            label="Reorder level"
            type="number"
            inputMode="decimal"
            min={0}
            step="any"
            trailing={baseUnitCode}
            value={form.reorderLevel}
            onChange={(e) => set("reorderLevel", e.target.value)}
            error={errors.reorderLevel}
            hint="Low-stock alerts fire at or below this available quantity."
          />
          <Input
            label="Reorder quantity"
            type="number"
            inputMode="decimal"
            min={0}
            step="any"
            trailing={baseUnitCode}
            value={form.reorderQuantity}
            onChange={(e) => set("reorderQuantity", e.target.value)}
            error={errors.reorderQuantity}
            hint="Suggested quantity to reorder."
          />
        </div>

        <div>
          <div className="mb-2 flex items-center justify-between gap-3">
            <div>
              <h3 className="text-xs font-semibold uppercase tracking-wide text-slate-500">Alternate units</h3>
              <p className="text-xs text-slate-500">Packaging units with a conversion to the base unit (e.g. 1 BOX = 12 {baseUnitCode}).</p>
            </div>
            <Button
              size="sm"
              variant="secondary"
              icon={<Plus className="size-3.5" />}
              disabled={!form.baseUnitId || availableForNewRow === 0}
              onClick={() => setForm((f) => ({ ...f, units: [...f.units, emptyUnitRow()] }))}
            >
              Add unit
            </Button>
          </div>
          {form.units.length === 0 ? (
            <p className="rounded-md border border-dashed border-slate-300 px-3 py-3 text-center text-xs text-slate-500">No alternate units.</p>
          ) : (
            <ul className="space-y-3">
              {form.units.map((row, i) => (
                <li key={row.key} className="grid grid-cols-1 gap-3 rounded-md border border-slate-200 bg-slate-50/60 p-3 sm:grid-cols-[1.3fr_1fr_1.2fr_auto] sm:items-start">
                  <Select
                    label="Unit"
                    value={row.unitId}
                    onChange={(e) => updateUnit(i, { unitId: e.target.value })}
                    error={errors[`units[${i}].unitId`]}
                    placeholder="Select unit"
                    options={unitOptionsFor(i)}
                    required
                  />
                  <Input
                    label="Conversion factor"
                    type="number"
                    inputMode="decimal"
                    min={0}
                    step="any"
                    trailing={`× ${baseUnitCode}`}
                    value={row.conversionFactor}
                    onChange={(e) => updateUnit(i, { conversionFactor: e.target.value })}
                    error={errors[`units[${i}].conversionFactor`]}
                    required
                  />
                  <Input
                    label="Barcode"
                    value={row.barcode}
                    onChange={(e) => updateUnit(i, { barcode: e.target.value })}
                    error={errors[`units[${i}].barcode`]}
                    maxLength={64}
                    className="font-mono"
                    placeholder="Optional"
                  />
                  <div className="flex sm:pt-6">
                    <Button
                      variant="danger-ghost"
                      size="icon"
                      aria-label={`Remove alternate unit ${i + 1}`}
                      onClick={() => setForm((f) => ({ ...f, units: f.units.filter((_, j) => j !== i) }))}
                    >
                      <Trash2 className="size-4" />
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          )}
          {errors.units && <p className="mt-1 text-xs font-medium text-red-600">{errors.units}</p>}
        </div>

        {mode === "edit" && (
          <Checkbox
            label="Active"
            description="Inactive products are hidden from pickers and cannot be received into stock."
            checked={form.isActive}
            onChange={(e) => set("isActive", e.target.checked)}
          />
        )}

        <FormError message={conflict ? null : formError} />
      </div>
    </Dialog>
  );
}
