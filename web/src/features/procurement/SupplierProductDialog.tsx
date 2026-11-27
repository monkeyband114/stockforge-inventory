"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Checkbox } from "@/components/ui/Checkbox";
import { Dialog } from "@/components/ui/Dialog";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { useToast } from "@/components/ui/Toast";
import { ProductPicker, type PickedProduct } from "@/features/common/ProductPicker";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { parseNumber } from "@/lib/format";
import { qk } from "@/lib/queries";
import type { SupplierProduct, SupplierProductRequest } from "@/lib/types";

/** Adds or edits a supplier's terms for one product (PUT /api/suppliers/{id}/products is an upsert). */
export function SupplierProductDialog({
  open,
  onClose,
  supplierId,
  existing,
  excludeIds,
}: {
  open: boolean;
  onClose: () => void;
  supplierId: string;
  existing?: SupplierProduct | null;
  excludeIds: string[];
}) {
  const editing = !!existing;
  const toast = useToast();
  const queryClient = useQueryClient();
  const [product, setProduct] = useState<PickedProduct | null>(null);
  const [supplierSku, setSupplierSku] = useState("");
  const [unitCost, setUnitCost] = useState("");
  const [moq, setMoq] = useState("");
  const [lead, setLead] = useState("");
  const [preferred, setPreferred] = useState(false);
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setProduct(existing ? { id: existing.productId, sku: existing.sku, name: existing.productName } : null);
    setSupplierSku(existing?.supplierSku ?? "");
    setUnitCost(existing ? String(existing.unitCost) : "");
    setMoq(existing ? String(existing.minimumOrderQuantity) : "");
    setLead(existing?.leadTimeDays != null ? String(existing.leadTimeDays) : "");
    setPreferred(existing?.isPreferred ?? false);
    setErrors({});
    setFormError(null);
  }, [open, existing]);

  const mutation = useMutation({
    mutationFn: (body: SupplierProductRequest) => api.put<SupplierProduct[]>(`/api/suppliers/${supplierId}/products`, body),
    onSuccess: (products) => {
      queryClient.setQueryData(qk.supplierProducts(supplierId), products);
      void queryClient.invalidateQueries({ queryKey: ["suppliers"] });
      void queryClient.invalidateQueries({ queryKey: ["purchase-orders", "suggestions"] });
      toast.success(editing ? "Supplier terms updated" : "Product added to supplier");
      onClose();
    },
    onError: (err) => {
      setErrors(fieldErrors(err));
      setFormError(err instanceof Error ? err.message : "Save failed.");
      if (!(err instanceof ApiError && err.status === 400)) toast.error(err);
    },
  });

  const submit = () => {
    const cost = parseNumber(unitCost);
    const min = moq.trim() ? parseNumber(moq) : 0;
    const days = lead.trim() ? parseNumber(lead) : null;
    const e = {
      productId: product ? undefined : "Choose a product.",
      unitCost: Number.isNaN(cost) ? "Unit cost is required." : cost < 0 ? "Must be zero or more." : Math.round(cost * 10_000) / 10_000 !== cost ? "At most 4 decimal places." : undefined,
      minimumOrderQuantity: Number.isNaN(min) || min < 0 ? "Must be zero or more." : undefined,
      leadTimeDays: days !== null && (!Number.isInteger(days) || days < 0 || days > 365) ? "Whole days between 0 and 365." : undefined,
      supplierSku: supplierSku.length > 64 ? "At most 64 characters." : undefined,
    };
    setErrors(e);
    setFormError(null);
    if (Object.values(e).some(Boolean)) return;
    mutation.mutate({ productId: product!.id, supplierSku: supplierSku.trim() || null, unitCost: cost, minimumOrderQuantity: min, leadTimeDays: days, isPreferred: preferred });
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      busy={mutation.isPending}
      title={editing ? `Edit terms — ${existing?.productName}` : "Add supplier product"}
      description="Negotiated terms are used as the default cost on new purchase-order lines for this supplier."
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
          <Button type="submit" loading={mutation.isPending}>
            {editing ? "Save terms" : "Add product"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <ProductPicker value={product} onChange={setProduct} required error={errors.productId} disabled={editing} excludeIds={excludeIds} />
        <div className="grid gap-3 sm:grid-cols-2">
          <Input label="Supplier SKU" value={supplierSku} onChange={(e) => setSupplierSku(e.target.value)} error={errors.supplierSku} maxLength={64} />
          <Input label="Unit cost" required type="number" inputMode="decimal" min={0} step="any" value={unitCost} onChange={(e) => setUnitCost(e.target.value)} error={errors.unitCost} />
          <Input label="Minimum order quantity" type="number" inputMode="decimal" min={0} step="any" value={moq} onChange={(e) => setMoq(e.target.value)} error={errors.minimumOrderQuantity} hint="Suggestions round up to this." />
          <Input label="Lead time" type="number" min={0} max={365} step={1} trailing="days" value={lead} onChange={(e) => setLead(e.target.value)} error={errors.leadTimeDays} />
        </div>
        <Checkbox
          label="Preferred supplier for this product"
          description="Only one supplier can be preferred per product; marking this one replaces any other."
          checked={preferred}
          onChange={(e) => setPreferred(e.target.checked)}
        />
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}
