"use client";

import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Save, Send, Trash2 } from "lucide-react";
import { useRouter } from "next/navigation";
import { useRef, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { controlClasses, FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Textarea } from "@/components/ui/Textarea";
import { useToast } from "@/components/ui/Toast";
import { ProductPicker, type PickedProduct } from "@/features/common/ProductPicker";
import { roundMoney } from "@/features/sales/orderMath";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { cn } from "@/lib/cn";
import { formatCurrency, formatQty, parseNumber } from "@/lib/format";
import { useIdempotentSubmission } from "@/lib/hooks";
import { qk, useCurrency, useWarehouses } from "@/lib/queries";
import type { CreatePurchaseOrderRequest, Product, PurchaseOrder, PurchaseOrderLineRequest, SupplierProduct, UpdatePurchaseOrderRequest } from "@/lib/types";
import { quantityError } from "@/lib/validation";
import type { PoPrefill } from "./poPrefill";
import { SupplierPicker, type PickedSupplier } from "./SupplierPicker";

interface LineState {
  key: number;
  product: PickedProduct | null;
  quantity: string;
  unitCost: string;
  taxPercent: string;
}

let seq = 0;
const newLine = (): LineState => ({ key: ++seq, product: null, quantity: "", unitCost: "", taxPercent: "" });

/** Mirrors PurchaseOrderLine pricing in src/StockForge.Domain/Procurement/Procurement.cs. */
function priceLine(qty: number, cost: number, taxRate: number) {
  if (!(qty > 0) || !(cost >= 0)) return { subtotal: 0, tax: 0, total: 0 };
  const subtotal = roundMoney(qty * cost);
  const tax = roundMoney(subtotal * taxRate);
  return { subtotal, tax, total: subtotal + tax };
}

export function PurchaseOrderForm({ order, prefill }: { order?: PurchaseOrder; prefill?: PoPrefill | null }) {
  const editing = !!order;
  const router = useRouter();
  const toast = useToast();
  const queryClient = useQueryClient();
  const currency = useCurrency();
  const warehouses = useWarehouses();
  const createSubmission = useIdempotentSubmission();
  const submitSubmission = useIdempotentSubmission();
  const intentRef = useRef<"draft" | "submit">("draft");
  const versionRef = useRef(order?.version ?? null);

  const [supplier, setSupplier] = useState<PickedSupplier | null>(
    order ? { id: order.supplierId, code: "", name: order.supplierName } : (prefill?.supplier ?? null),
  );
  const [warehouseId, setWarehouseId] = useState(order?.warehouseId ?? prefill?.warehouseId ?? "");
  const [expectedDate, setExpectedDate] = useState(order?.expectedDate ?? "");
  const [notes, setNotes] = useState(order?.notes ?? "");
  const [lines, setLines] = useState<LineState[]>(() => {
    if (order)
      return order.lines.map((l) => ({
        key: ++seq,
        product: { id: l.productId, sku: l.sku, name: l.productName },
        quantity: String(l.quantity),
        unitCost: String(l.unitCost),
        taxPercent: l.taxRate ? String(Number((l.taxRate * 100).toFixed(4))) : "",
      }));
    if (prefill?.lines.length)
      return prefill.lines.map((l) => ({ key: ++seq, product: l.product, quantity: String(l.quantity), unitCost: l.unitCost != null ? String(l.unitCost) : "", taxPercent: "" }));
    return [newLine()];
  });
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);

  const onlyWarehouse = warehouses.data?.length === 1 ? warehouses.data[0].id : "";
  const effectiveWarehouse = warehouseId || onlyWarehouse;

  // Supplier's negotiated terms → default unit cost (the server applies the same fallback).
  const terms = useQuery({
    queryKey: qk.supplierProducts(supplier?.id ?? ""),
    queryFn: () => api.get<SupplierProduct[]>(`/api/suppliers/${supplier!.id}/products`),
    enabled: !!supplier,
    staleTime: 60_000,
  });
  const termsFor = (productId: string | undefined) => terms.data?.find((t) => t.productId === productId);
  const productQueries = useQueries({
    queries: lines.map((l) => ({
      queryKey: qk.product(l.product?.id ?? ""),
      queryFn: () => api.get<Product>(`/api/products/${l.product!.id}`),
      enabled: !!l.product,
      staleTime: 60_000,
    })),
  });

  const priced = lines.map((l, i) => {
    const t = termsFor(l.product?.id);
    const defaultCost = t?.unitCost ?? productQueries[i]?.data?.costPrice ?? 0;
    const cost = l.unitCost.trim() ? parseNumber(l.unitCost) : defaultCost;
    const rate = l.taxPercent.trim() ? parseNumber(l.taxPercent) / 100 : 0;
    return { defaultCost, terms: t, ...priceLine(parseNumber(l.quantity), cost, rate) };
  });
  const totals = priced.reduce((a, p) => ({ subtotal: roundMoney(a.subtotal + p.subtotal), tax: roundMoney(a.tax + p.tax), total: roundMoney(a.total + p.total) }), { subtotal: 0, tax: 0, total: 0 });

  const updateLine = (key: number, patch: Partial<LineState>) => {
    setLines((ls) => ls.map((l) => (l.key === key ? { ...l, ...patch } : l)));
    setErrors((e) => ({ ...e, [`line-${key}`]: undefined }));
  };

  const buildLines = (): PurchaseOrderLineRequest[] =>
    lines
      .filter((l) => l.product)
      .map((l) => ({
        productId: l.product!.id,
        quantity: parseNumber(l.quantity),
        unitCost: l.unitCost.trim() ? parseNumber(l.unitCost) : null,
        taxRate: l.taxPercent.trim() ? Number((parseNumber(l.taxPercent) / 100).toFixed(5)) : null,
      }));

  const validate = () => {
    const e: Record<string, string | undefined> = {};
    if (!supplier) e.supplierId = "Choose a supplier.";
    if (!effectiveWarehouse) e.warehouseId = "Choose the receiving warehouse.";
    const filled = lines.filter((l) => l.product || l.quantity);
    if (filled.length === 0) e.lines = "Add at least one product.";
    const seen = new Set<string>();
    for (const l of filled) {
      const k = `line-${l.key}`;
      const cost = l.unitCost.trim() ? parseNumber(l.unitCost) : 0;
      const tax = l.taxPercent.trim() ? parseNumber(l.taxPercent) : 0;
      if (!l.product) e[k] = "Choose a product.";
      else if (seen.has(l.product.id)) e[k] = "Each product may appear only once.";
      else {
        seen.add(l.product.id);
        e[k] =
          quantityError(parseNumber(l.quantity)) ??
          (Number.isNaN(cost) || cost < 0 ? "Unit cost must be zero or more." : Math.round(cost * 10_000) / 10_000 !== cost ? "Unit cost can have at most 4 decimals." : undefined) ??
          (Number.isNaN(tax) || tax < 0 || tax > 100 ? "Tax rate must be between 0 and 100%." : undefined);
      }
    }
    return e;
  };

  const mutation = useMutation({
    mutationFn: async (intent: "draft" | "submit"): Promise<{ order: PurchaseOrder; replayed: boolean }> => {
      const common = { supplierId: supplier!.id, warehouseId: effectiveWarehouse, expectedDate: expectedDate || null, notes: notes.trim() || null, lines: buildLines() };
      if (editing && order) {
        const body: UpdatePurchaseOrderRequest = { ...common, version: versionRef.current };
        let saved = await api.put<PurchaseOrder>(`/api/purchase-orders/${order.id}`, body);
        versionRef.current = saved.version;
        if (intent === "submit") {
          saved = (await api.postIdempotent<PurchaseOrder>(`/api/purchase-orders/${order.id}/submit`, {}, submitSubmission.keyFor({ id: order.id, v: saved.version }))).data;
        }
        return { order: saved, replayed: false };
      }
      const body: CreatePurchaseOrderRequest = { ...common, submit: intent === "submit" };
      const result = await api.postIdempotent<PurchaseOrder>("/api/purchase-orders", body, createSubmission.keyFor(body));
      return { order: result.data, replayed: result.replayed };
    },
    onSuccess: ({ order: saved, replayed }) => {
      createSubmission.reset();
      submitSubmission.reset();
      for (const root of ["purchase-orders", "suppliers"]) void queryClient.invalidateQueries({ queryKey: [root] });
      queryClient.setQueryData(qk.purchaseOrder(saved.id), saved);
      if (replayed) toast.info(`${saved.number} already exists`, "This submission was processed earlier — no duplicate was created.");
      else if (saved.status === "Draft") toast.success(`Draft ${saved.number} saved`);
      else if (saved.status === "PendingApproval") toast.info(`${saved.number} submitted for approval`, "The total is above the purchase approval threshold.");
      else toast.success(`${saved.number} approved`, "Within the approval threshold — ready to send to the supplier.");
      router.push(`/purchase-orders/${saved.id}`);
    },
    onError: (err) => {
      if (err instanceof ApiError) {
        if (err.code === "concurrency_conflict") {
          setFormError("Someone else changed this purchase order. Reload the page to get the latest version.");
          return;
        }
        const fe = fieldErrors(err);
        const mapped: Record<string, string | undefined> = { supplierId: fe.supplierId, warehouseId: fe.warehouseId, notes: fe.notes, lines: fe.lines };
        lines
          .filter((l) => l.product)
          .forEach((l, i) => {
            const msg = fe[`lines[${i}].quantity`] ?? fe[`lines[${i}].unitCost`] ?? fe[`lines[${i}].taxRate`] ?? fe[`lines[${i}].productId`];
            if (msg) mapped[`line-${l.key}`] = msg;
          });
        setErrors(mapped);
        setFormError(err.detail);
        if (err.status !== 400) toast.error(err);
        return;
      }
      setFormError(err instanceof Error ? err.message : "Could not save the purchase order.");
    },
  });

  const save = (intent: "draft" | "submit") => {
    intentRef.current = intent;
    const e = validate();
    setErrors(e);
    setFormError(null);
    if (Object.values(e).some(Boolean)) return;
    setLines((ls) => ls.filter((l) => l.product || l.quantity));
    mutation.mutate(intent);
  };
  const pending = mutation.isPending;

  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault();
        save("submit");
      }}
      className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]"
    >
      <div className="min-w-0 space-y-6">
        <Card>
          <CardHeader title="Purchase order" />
          <CardBody className="space-y-4">
            <div className="grid gap-4 md:grid-cols-3">
              <SupplierPicker value={supplier} onChange={setSupplier} error={errors.supplierId} required />
              <Select
                label="Receiving warehouse"
                required
                value={effectiveWarehouse}
                onChange={(e) => setWarehouseId(e.target.value)}
                error={errors.warehouseId}
                placeholder="Select warehouse…"
                options={(warehouses.data ?? []).map((w) => ({ value: w.id, label: `${w.code} — ${w.name}` }))}
              />
              <Input label="Expected delivery" type="date" value={expectedDate} onChange={(e) => setExpectedDate(e.target.value)} />
            </div>
            <Textarea label="Notes" value={notes} onChange={(e) => setNotes(e.target.value)} error={errors.notes} maxLength={2000} rows={2} />
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Lines" description="Leave the unit cost blank to use the supplier's negotiated cost (or the product cost price)." />
          <CardBody className="space-y-3">
            {errors.lines && <p className="text-xs font-medium text-red-600">{errors.lines}</p>}
            <ul className="space-y-3">
              {lines.map((line, index) => {
                const p = priced[index];
                return (
                  <li key={line.key} className="rounded-md border border-slate-200 p-3">
                    <div className="grid items-start gap-3 lg:grid-cols-[minmax(0,1fr)_7rem_8rem_6rem_auto]">
                      <ProductPicker
                        label={`Product ${index + 1}`}
                        value={line.product}
                        onChange={(product) => updateLine(line.key, { product, unitCost: "" })}
                        excludeIds={lines.filter((l) => l.key !== line.key && l.product).map((l) => l.product!.id)}
                      />
                      <label className="flex flex-col gap-1.5">
                        <span className="text-sm font-medium text-slate-700">Quantity</span>
                        <input
                          type="number"
                          inputMode="decimal"
                          min={0}
                          step="any"
                          value={line.quantity}
                          onChange={(e) => updateLine(line.key, { quantity: e.target.value })}
                          aria-invalid={errors[`line-${line.key}`] ? true : undefined}
                          className={cn(controlClasses, "h-9")}
                        />
                      </label>
                      <label className="flex flex-col gap-1.5">
                        <span className="text-sm font-medium text-slate-700">Unit cost</span>
                        <input
                          type="number"
                          inputMode="decimal"
                          min={0}
                          step="any"
                          value={line.unitCost}
                          placeholder={line.product ? String(p.defaultCost) : "Default"}
                          onChange={(e) => updateLine(line.key, { unitCost: e.target.value })}
                          className={cn(controlClasses, "h-9")}
                        />
                      </label>
                      <label className="flex flex-col gap-1.5">
                        <span className="text-sm font-medium text-slate-700">Tax %</span>
                        <input
                          type="number"
                          inputMode="decimal"
                          min={0}
                          max={100}
                          step="any"
                          value={line.taxPercent}
                          placeholder="0"
                          onChange={(e) => updateLine(line.key, { taxPercent: e.target.value })}
                          className={cn(controlClasses, "h-9")}
                        />
                      </label>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="mt-6"
                        onClick={() => setLines((ls) => (ls.length > 1 ? ls.filter((l) => l.key !== line.key) : [newLine()]))}
                        aria-label={`Remove line ${index + 1}`}
                      >
                        <Trash2 className="size-4" />
                      </Button>
                    </div>
                    <div className="mt-2 flex flex-wrap items-center justify-between gap-2 text-xs">
                      <span>
                        {line.product && supplier && (
                          <span className="text-slate-500">
                            {p.terms
                              ? `Supplier terms: ${formatCurrency(p.terms.unitCost, currency, { precise: true })}${p.terms.minimumOrderQuantity ? ` · MOQ ${formatQty(p.terms.minimumOrderQuantity)}` : ""}${p.terms.leadTimeDays != null ? ` · ${p.terms.leadTimeDays} d lead time` : ""}`
                              : terms.isPending
                                ? "Loading supplier terms…"
                                : "No negotiated terms with this supplier — product cost price applies."}
                          </span>
                        )}
                        {errors[`line-${line.key}`] && <span className="ml-3 font-medium text-red-600">{errors[`line-${line.key}`]}</span>}
                      </span>
                      {line.product && p.total > 0 && (
                        <span className="tabular-nums text-slate-500">
                          Line total <span className="font-semibold text-slate-900">{formatCurrency(p.total, currency)}</span>
                        </span>
                      )}
                    </div>
                  </li>
                );
              })}
            </ul>
            <Button variant="secondary" size="sm" icon={<Plus className="size-3.5" />} onClick={() => setLines((ls) => [...ls, newLine()])}>
              Add line
            </Button>
          </CardBody>
        </Card>
      </div>

      <div className="space-y-4 xl:sticky xl:top-20 xl:self-start">
        <Card>
          <CardHeader title="Summary" description="Preview — the server calculates the final amounts." />
          <CardBody>
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between">
                <dt className="text-slate-500">Subtotal</dt>
                <dd className="tabular-nums">{formatCurrency(totals.subtotal, currency)}</dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-slate-500">Tax</dt>
                <dd className="tabular-nums">{formatCurrency(totals.tax, currency)}</dd>
              </div>
              <div className="flex justify-between border-t border-slate-200 pt-2 text-base font-semibold">
                <dt>Total</dt>
                <dd className="tabular-nums">{formatCurrency(totals.total, currency)}</dd>
              </div>
            </dl>
          </CardBody>
        </Card>
        <FormError message={formError} />
        <div className="flex flex-col gap-2">
          <Button type="submit" icon={<Send className="size-4" />} loading={pending && intentRef.current === "submit"} disabled={pending}>
            Save &amp; submit
          </Button>
          <Button variant="secondary" icon={<Save className="size-4" />} onClick={() => save("draft")} loading={pending && intentRef.current === "draft"} disabled={pending}>
            Save draft
          </Button>
          <p className="text-xs text-slate-500">Orders within the purchase approval threshold are approved automatically on submit; larger ones wait for an approver.</p>
        </div>
      </div>
    </form>
  );
}
