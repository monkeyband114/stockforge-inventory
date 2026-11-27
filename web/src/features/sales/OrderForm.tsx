"use client";

import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Save, Send, Trash2 } from "lucide-react";
import { useRouter } from "next/navigation";
import { useRef, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { Checkbox } from "@/components/ui/Checkbox";
import { controlClasses, FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Textarea } from "@/components/ui/Textarea";
import { useToast } from "@/components/ui/Toast";
import { ProductPicker, type PickedProduct } from "@/features/common/ProductPicker";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { cn } from "@/lib/cn";
import { formatCurrency, formatPercent, formatQty, parseNumber } from "@/lib/format";
import { useIdempotentSubmission } from "@/lib/hooks";
import { qk, useAccessibleWarehouses, useCurrency, useOrganization } from "@/lib/queries";
import type { CreateOrderRequest, InventoryBalance, Order, OrderLineRequest, PagedResult, Product, UpdateOrderRequest } from "@/lib/types";
import { quantityError } from "@/lib/validation";
import { AddressFields, addressErrors, addressToForm, emptyAddress, formToAddress, type AddressForm } from "./AddressFields";
import { CustomerPicker, type PickedCustomer } from "./CustomerPicker";
import { priceLine, sumLines } from "./orderMath";

interface LineState {
  key: number;
  product: PickedProduct | null;
  quantity: string;
  unitPrice: string;
  discount: string;
}

let seq = 0;
const newLine = (product: PickedProduct | null = null): LineState => ({ key: ++seq, product, quantity: "", unitPrice: "", discount: "" });

function AvailableAt({ warehouseId, productId, requested }: { warehouseId: string; productId: string; requested: number }) {
  const params = { warehouseId, productId, pageSize: 1 };
  const q = useQuery({
    queryKey: qk.inventory({ ...params, single: true }),
    queryFn: () => api.get<PagedResult<InventoryBalance>>("/api/inventory", params),
    enabled: !!warehouseId && !!productId,
  });
  if (!warehouseId) return <span className="text-xs text-slate-400">Choose a warehouse</span>;
  if (q.isPending) return <span className="text-xs text-slate-400">…</span>;
  if (q.isError) return <span className="text-xs text-slate-400">Availability unavailable</span>;
  const available = q.data?.items[0]?.quantityAvailable ?? 0;
  const short = requested > available;
  return (
    <span className={cn("text-xs tabular-nums", short ? "font-medium text-amber-700" : "text-slate-500")}>
      {formatQty(available)} available{short ? " — not enough to fulfil" : ""}
    </span>
  );
}

const pct = (v: string) => (v.trim() ? parseNumber(v) : 0);

export function OrderForm({ order, initialCustomer }: { order?: Order; initialCustomer?: PickedCustomer | null }) {
  const editing = !!order;
  const router = useRouter();
  const toast = useToast();
  const queryClient = useQueryClient();
  const currency = useCurrency();
  const organization = useOrganization();
  const warehouses = useAccessibleWarehouses();
  const createSubmission = useIdempotentSubmission();
  const submitSubmission = useIdempotentSubmission();
  const intentRef = useRef<"draft" | "submit">("draft");
  /** Latest saved version: a failed submit after a successful save must not cause a false conflict on retry. */
  const versionRef = useRef(order?.version ?? null);

  const [customer, setCustomer] = useState<PickedCustomer | null>(
    order ? { id: order.customerId, code: "", name: order.customerName } : (initialCustomer ?? null),
  );
  const [warehouseId, setWarehouseId] = useState(order?.warehouseId ?? "");
  const [requiredDate, setRequiredDate] = useState(order?.requiredDate ?? "");
  const [customerReference, setCustomerReference] = useState(order?.customerReference ?? "");
  const [notes, setNotes] = useState(order?.notes ?? "");
  const [discount, setDiscount] = useState(order && order.discountPercent ? String(order.discountPercent) : "");
  const [useCustomerAddress, setUseCustomerAddress] = useState(!editing);
  const [address, setAddress] = useState<AddressForm>(order ? addressToForm(order.shippingAddress) : emptyAddress());
  const [lines, setLines] = useState<LineState[]>(() =>
    order
      ? order.lines.map((l) => ({
          key: ++seq,
          product: { id: l.productId, sku: l.sku, name: l.productName },
          quantity: String(l.quantity),
          unitPrice: String(l.unitPrice),
          discount: l.discountPercent ? String(l.discountPercent) : "",
        }))
      : [newLine()],
  );
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);

  // Default the warehouse when the member operates exactly one.
  const onlyWarehouse = warehouses.data?.length === 1 ? warehouses.data[0].id : "";
  const effectiveWarehouse = warehouseId || onlyWarehouse;

  // Product details give list price and tax rate for the live preview.
  const productQueries = useQueries({
    queries: lines.map((l) => ({
      queryKey: qk.product(l.product?.id ?? ""),
      queryFn: () => api.get<Product>(`/api/products/${l.product!.id}`),
      enabled: !!l.product,
      staleTime: 60_000,
    })),
  });

  const defaultTax = organization.data?.settings.defaultTaxRate ?? 0;
  const orderDiscount = pct(discount);
  const priced = lines.map((l, i) => {
    const product = productQueries[i]?.data;
    const qty = parseNumber(l.quantity);
    const listPrice = product?.sellingPrice ?? 0;
    const unitPrice = l.unitPrice.trim() ? parseNumber(l.unitPrice) : listPrice;
    const taxRate = product ? (product.taxRate ?? defaultTax) : 0;
    return { listPrice, unitPrice, taxRate, ...priceLine(qty, unitPrice, pct(l.discount), orderDiscount, taxRate) };
  });
  const totals = sumLines(priced);

  const updateLine = (key: number, patch: Partial<LineState>) => {
    setLines((ls) => ls.map((l) => (l.key === key ? { ...l, ...patch } : l)));
    setErrors((e) => ({ ...e, [`line-${key}`]: undefined }));
  };

  const buildLines = (): OrderLineRequest[] =>
    lines
      .filter((l) => l.product)
      .map((l) => ({
        productId: l.product!.id,
        quantity: parseNumber(l.quantity),
        unitPrice: l.unitPrice.trim() ? parseNumber(l.unitPrice) : null,
        discountPercent: pct(l.discount),
      }));

  const validate = (): Record<string, string | undefined> => {
    const e: Record<string, string | undefined> = {};
    if (!customer) e.customerId = "Choose a customer.";
    if (!effectiveWarehouse) e.warehouseId = "Choose the fulfillment warehouse.";
    if (Number.isNaN(orderDiscount) || orderDiscount < 0 || orderDiscount > 100) e.discountPercent = "Between 0 and 100%.";
    if (customerReference.length > 64) e.customerReference = "At most 64 characters.";
    const filled = lines.filter((l) => l.product || l.quantity);
    if (filled.length === 0) e.lines = "Add at least one product.";
    const seen = new Set<string>();
    for (const l of filled) {
      const k = `line-${l.key}`;
      if (!l.product) e[k] = "Choose a product.";
      else if (seen.has(l.product.id)) e[k] = "Each product may appear only once.";
      else {
        seen.add(l.product.id);
        const qe = quantityError(parseNumber(l.quantity));
        const price = l.unitPrice.trim() ? parseNumber(l.unitPrice) : 0;
        const d = pct(l.discount);
        if (qe) e[k] = qe;
        else if (Number.isNaN(price) || price < 0) e[k] = "Unit price must be zero or more.";
        else if (Math.round(price * 100) / 100 !== price) e[k] = "Unit price can have at most 2 decimals.";
        else if (Number.isNaN(d) || d < 0 || d > 100) e[k] = "Line discount must be between 0 and 100%.";
      }
    }
    if (!useCustomerAddress || editing) {
      const ae = addressErrors(address);
      e["address.line1"] = ae.line1;
      e["address.city"] = ae.city;
      e["address.country"] = ae.country;
    }
    return e;
  };

  const mutation = useMutation({
    mutationFn: async (intent: "draft" | "submit"): Promise<{ order: Order; replayed: boolean }> => {
      const common = {
        customerId: customer!.id,
        warehouseId: effectiveWarehouse,
        requiredDate: requiredDate || null,
        customerReference: customerReference.trim() || null,
        notes: notes.trim() || null,
        discountPercent: orderDiscount,
        shippingAddress: useCustomerAddress && !editing ? null : formToAddress(address),
        lines: buildLines(),
      };
      if (editing && order) {
        const body: UpdateOrderRequest = { ...common, version: versionRef.current };
        let saved = await api.put<Order>(`/api/orders/${order.id}`, body);
        versionRef.current = saved.version;
        if (intent === "submit") {
          // Submit requires an Idempotency-Key; keyed per order version so a retry replays.
          const result = await api.postIdempotent<Order>(`/api/orders/${order.id}/submit`, {}, submitSubmission.keyFor({ id: order.id, v: saved.version }));
          saved = result.data;
        }
        return { order: saved, replayed: false };
      }
      const body: CreateOrderRequest = { ...common, submit: intent === "submit" };
      const result = await api.postIdempotent<Order>("/api/orders", body, createSubmission.keyFor(body));
      return { order: result.data, replayed: result.replayed };
    },
    onSuccess: ({ order: saved, replayed }) => {
      createSubmission.reset();
      submitSubmission.reset();
      for (const root of ["orders", "customers", "dashboard", "inventory", "reservations", "fulfillment", "invoices", "finance"]) {
        void queryClient.invalidateQueries({ queryKey: [root] });
      }
      queryClient.setQueryData(qk.order(saved.id), saved);
      if (replayed) toast.info(`Order ${saved.number} already exists`, "This submission was processed earlier — no duplicate was created.");
      else if (saved.status === "Draft") toast.success(`Draft ${saved.number} saved`);
      else if (saved.status === "PendingApproval") toast.info(`Order ${saved.number} submitted for approval`, saved.approvalReason ?? undefined);
      else toast.success(`Order ${saved.number} approved`, "Stock is reserved and the invoice has been issued.");
      router.push(`/orders/${saved.id}`);
    },
    onError: (err) => {
      if (err instanceof ApiError) {
        if (err.code === "insufficient_stock") {
          const productId = err.extra<string>("productId");
          const available = err.extra<number>("available");
          const line = lines.find((l) => l.product?.id === productId);
          if (line) setErrors({ [`line-${line.key}`]: `Only ${formatQty(available ?? 0)} available at this warehouse.` });
          setFormError(`${err.detail} The order was not submitted.`);
          return;
        }
        if (err.code === "concurrency_conflict") {
          setFormError("Someone else changed this order while you were editing. Reload the page to get the latest version.");
          return;
        }
        const fe = fieldErrors(err);
        const mapped: Record<string, string | undefined> = {
          customerId: fe.customerId,
          warehouseId: fe.warehouseId,
          discountPercent: fe.discountPercent,
          customerReference: fe.customerReference,
          notes: fe.notes,
          lines: fe.lines,
          "address.line1": fe["shippingAddress.line1"],
          "address.city": fe["shippingAddress.city"],
          "address.country": fe["shippingAddress.country"],
        };
        const filled = lines.filter((l) => l.product);
        filled.forEach((l, i) => {
          const msg = fe[`lines[${i}].quantity`] ?? fe[`lines[${i}].unitPrice`] ?? fe[`lines[${i}].discountPercent`] ?? fe[`lines[${i}].productId`];
          if (msg) mapped[`line-${l.key}`] = msg;
        });
        setErrors(mapped);
        setFormError(err.detail);
        if (err.status !== 400) toast.error(err);
        return;
      }
      setFormError(err instanceof Error ? err.message : "Could not save the order.");
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
  const warehouseOptions = (warehouses.data ?? []).map((w) => ({ value: w.id, label: `${w.code} — ${w.name}` }));

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
          <CardHeader title="Order details" />
          <CardBody className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <CustomerPicker value={customer} onChange={setCustomer} error={errors.customerId} required />
              <Select
                label="Fulfillment warehouse"
                required
                value={effectiveWarehouse}
                onChange={(e) => setWarehouseId(e.target.value)}
                error={errors.warehouseId}
                placeholder="Select warehouse…"
                options={warehouseOptions}
                hint="Only warehouses you can operate are listed."
              />
            </div>
            <div className="grid gap-4 md:grid-cols-3">
              <Input label="Required by" type="date" value={requiredDate} onChange={(e) => setRequiredDate(e.target.value)} />
              <Input
                label="Customer reference"
                value={customerReference}
                onChange={(e) => setCustomerReference(e.target.value)}
                error={errors.customerReference}
                maxLength={64}
                placeholder="e.g. customer PO number"
              />
              <Input
                label="Order discount"
                type="number"
                min={0}
                max={100}
                step="any"
                trailing="%"
                value={discount}
                onChange={(e) => setDiscount(e.target.value)}
                error={errors.discountPercent}
              />
            </div>
            <Textarea label="Notes" value={notes} onChange={(e) => setNotes(e.target.value)} error={errors.notes} maxLength={2000} rows={2} />
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Lines" description="Prices default to each product's selling price; override per line if agreed with the customer." />
          <CardBody className="space-y-3">
            {errors.lines && <p className="text-xs font-medium text-red-600">{errors.lines}</p>}
            <ul className="space-y-3">
              {lines.map((line, index) => {
                const p = priced[index];
                const qty = parseNumber(line.quantity);
                return (
                  <li key={line.key} className="rounded-md border border-slate-200 p-3">
                    <div className="grid items-start gap-3 lg:grid-cols-[minmax(0,1fr)_7rem_8rem_6.5rem_auto]">
                      <ProductPicker
                        label={`Product ${index + 1}`}
                        value={line.product}
                        onChange={(product) => updateLine(line.key, { product, unitPrice: "" })}
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
                        <span className="text-sm font-medium text-slate-700">Unit price</span>
                        <input
                          type="number"
                          inputMode="decimal"
                          min={0}
                          step="0.01"
                          value={line.unitPrice}
                          placeholder={line.product && p.listPrice ? String(p.listPrice) : "List price"}
                          onChange={(e) => updateLine(line.key, { unitPrice: e.target.value })}
                          className={cn(controlClasses, "h-9")}
                        />
                      </label>
                      <label className="flex flex-col gap-1.5">
                        <span className="text-sm font-medium text-slate-700">Discount %</span>
                        <input
                          type="number"
                          inputMode="decimal"
                          min={0}
                          max={100}
                          step="any"
                          value={line.discount}
                          onChange={(e) => updateLine(line.key, { discount: e.target.value })}
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
                    <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
                      <span>
                        {line.product && <AvailableAt warehouseId={effectiveWarehouse} productId={line.product.id} requested={Number.isNaN(qty) ? 0 : qty} />}
                        {errors[`line-${line.key}`] && <span className="ml-3 text-xs font-medium text-red-600">{errors[`line-${line.key}`]}</span>}
                      </span>
                      {line.product && p.total > 0 && (
                        <span className="text-xs tabular-nums text-slate-500">
                          Tax {formatPercent(p.taxRate)} · Line total <span className="font-semibold text-slate-900">{formatCurrency(p.total, currency)}</span>
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

        <Card>
          <CardHeader title="Shipping address" />
          <CardBody className="space-y-3">
            {!editing && (
              <Checkbox
                label="Ship to the customer's address on file"
                description="Uncheck to deliver this order somewhere else."
                checked={useCustomerAddress}
                onChange={(e) => setUseCustomerAddress(e.target.checked)}
              />
            )}
            {(!useCustomerAddress || editing) && (
              <AddressFields
                value={address}
                onChange={setAddress}
                errors={{ line1: errors["address.line1"], city: errors["address.city"], country: errors["address.country"] }}
              />
            )}
          </CardBody>
        </Card>
      </div>

      <div className="space-y-4 xl:sticky xl:top-20 xl:self-start">
        <Card>
          <CardHeader title="Summary" description="Preview — final amounts are calculated by the server." />
          <CardBody>
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between">
                <dt className="text-slate-500">Subtotal</dt>
                <dd className="tabular-nums text-slate-900">{formatCurrency(totals.subtotal, currency)}</dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-slate-500">Discounts</dt>
                <dd className="tabular-nums text-slate-900">{totals.discount ? `−${formatCurrency(totals.discount, currency)}` : formatCurrency(0, currency)}</dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-slate-500">Tax</dt>
                <dd className="tabular-nums text-slate-900">{formatCurrency(totals.tax, currency)}</dd>
              </div>
              <div className="flex justify-between border-t border-slate-200 pt-2 text-base font-semibold">
                <dt>Total</dt>
                <dd className="tabular-nums">{formatCurrency(totals.total, currency)}</dd>
              </div>
            </dl>
            {customer?.creditLimit != null && (customer.openBalance ?? 0) + totals.total > customer.creditLimit && (
              <p className="mt-3 rounded-md bg-amber-50 px-3 py-2 text-xs text-amber-800">
                This order would take {customer.name} over its credit limit of {formatCurrency(customer.creditLimit, currency)} — it will need approval.
              </p>
            )}
          </CardBody>
        </Card>
        <FormError message={formError} />
        <div className="flex flex-col gap-2">
          <Button type="submit" icon={<Send className="size-4" />} loading={pending && intentRef.current === "submit"} disabled={pending}>
            Save &amp; submit
          </Button>
          <Button variant="secondary" icon={<Save className="size-4" />} onClick={() => save("draft")} loading={pending && intentRef.current === "draft"} disabled={pending}>
            {editing ? "Save draft" : "Save as draft"}
          </Button>
          <p className="text-xs text-slate-500">
            Submitting reserves stock right away when the order is within the approval thresholds; otherwise it waits for an approver.
          </p>
        </div>
      </div>
    </form>
  );
}
