"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Textarea } from "@/components/ui/Textarea";
import { useToast } from "@/components/ui/Toast";
import { ProductPicker, type PickedProduct } from "@/features/common/ProductPicker";
import { api, ApiError, fieldErrors, type ApiResult } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatQty, localDateTimeToIso, parseNumber } from "@/lib/format";
import { useIdempotentSubmission } from "@/lib/hooks";
import { P } from "@/lib/permissions";
import { qk, useAccessibleWarehouses, useProduct, useUnits } from "@/lib/queries";
import type { AdjustmentReason, InventoryBalance, PagedResult, StockAdjustment, StockMovement, StockReservation } from "@/lib/types";
import { quantityError } from "@/lib/validation";

export type InventoryAction = "stockIn" | "stockOut" | "damaged" | "return" | "count" | "correction" | "reserve";

interface ActionConfig {
  title: string;
  description: string;
  permission: string;
  path: string;
  submitLabel: string;
  usesUnits?: boolean;
}

export const ACTIONS: Record<InventoryAction, ActionConfig> = {
  stockIn: {
    title: "Stock in",
    description: "Receive units into a warehouse. The unit cost updates the moving average cost.",
    permission: P.inventoryReceive,
    path: "/api/inventory/stock-in",
    submitLabel: "Receive stock",
    usesUnits: true,
  },
  stockOut: {
    title: "Stock out",
    description: "Issue units out of a warehouse (sale, consumption, shipment).",
    permission: P.inventoryIssue,
    path: "/api/inventory/stock-out",
    submitLabel: "Issue stock",
    usesUnits: true,
  },
  damaged: {
    title: "Record damaged stock",
    description: "Write off damaged or unusable units. A reason is required.",
    permission: P.inventoryIssue,
    path: "/api/inventory/damaged",
    submitLabel: "Write off",
    usesUnits: true,
  },
  return: {
    title: "Record return",
    description: "Put returned units back into stock. A reason is required.",
    permission: P.inventoryReceive,
    path: "/api/inventory/returns",
    submitLabel: "Record return",
    usesUnits: true,
  },
  count: {
    title: "Cycle count",
    description: "Enter the physically counted quantity. Any variance becomes a stock adjustment (applied automatically for approvers).",
    permission: P.inventoryCount,
    path: "/api/stock-adjustments/counts",
    submitLabel: "Submit count",
  },
  correction: {
    title: "Stock correction",
    description: "Request a signed correction to on-hand stock. Depending on organization settings it may need approval.",
    permission: P.inventoryAdjustRequest,
    path: "/api/stock-adjustments/corrections",
    submitLabel: "Submit correction",
  },
  reserve: {
    title: "Reserve stock",
    description: "Place a manual hold so the units cannot be issued or transferred.",
    permission: P.inventoryReserve,
    path: "/api/reservations",
    submitLabel: "Create hold",
  },
};

export const ACTION_ORDER: InventoryAction[] = ["stockIn", "stockOut", "damaged", "return", "count", "correction", "reserve"];

const CORRECTION_REASONS: { value: Exclude<AdjustmentReason, "CycleCount">; label: string }[] = [
  { value: "Correction", label: "Correction" },
  { value: "Loss", label: "Loss / shrinkage" },
  { value: "Found", label: "Found stock" },
  { value: "Expired", label: "Expired" },
  { value: "Other", label: "Other" },
];

export interface ActionPreset {
  warehouseId: string;
  product: PickedProduct;
}

interface FormState {
  warehouseId: string;
  product: PickedProduct | null;
  quantity: string;
  unitId: string;
  unitCost: string;
  referenceNumber: string;
  text: string;
  reason: Exclude<AdjustmentReason, "CycleCount">;
  expiresAt: string;
}

const emptyForm = (preset?: ActionPreset | null, defaultWarehouse = ""): FormState => ({
  warehouseId: preset?.warehouseId ?? defaultWarehouse,
  product: preset?.product ?? null,
  quantity: "",
  unitId: "",
  unitCost: "",
  referenceNumber: "",
  text: "",
  reason: "Correction",
  expiresAt: "",
});

const nullable = (v: string) => (v.trim() ? v.trim() : null);

export function InventoryActionDialog({
  action,
  preset,
  onClose,
}: {
  action: InventoryAction | null;
  preset?: ActionPreset | null;
  onClose: () => void;
}) {
  const open = action !== null;
  const config = action ? ACTIONS[action] : null;
  const { hasPermission } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const warehouses = useAccessibleWarehouses();
  const units = useUnits();
  const submission = useIdempotentSubmission();

  const [form, setForm] = useState<FormState>(emptyForm(preset));
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);

  const singleWarehouse = warehouses.data?.length === 1 ? warehouses.data[0].id : "";

  useEffect(() => {
    if (open) {
      setForm(emptyForm(preset, singleWarehouse));
      setErrors({});
      setFormError(null);
      submission.reset();
    }
    // Only when the dialog (re)opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, action, preset]);

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => {
    setForm((f) => ({ ...f, [key]: value }));
    setErrors((e) => ({ ...e, [key]: undefined }));
  };

  const productId = form.product?.id ?? null;
  const product = useProduct(open && config?.usesUnits ? productId : null);

  // Current balance for context (on hand / available / variance preview).
  const balanceParams = { warehouseId: form.warehouseId, productId: productId ?? "", pageSize: 1 };
  const balance = useQuery({
    queryKey: qk.inventory({ ...balanceParams, single: true }),
    queryFn: () => api.get<PagedResult<InventoryBalance>>("/api/inventory", balanceParams),
    enabled: open && !!form.warehouseId && !!productId,
  });
  const current = balance.data?.items[0];
  const onHand = current?.quantityOnHand ?? 0;
  const available = current?.quantityAvailable ?? 0;
  const baseUnitCode = product.data?.baseUnitCode ?? current?.unitCode ?? form.product?.baseUnitCode ?? "";

  const unitOptions = useMemo(() => {
    const p = product.data;
    if (!p) return [];
    return [
      { value: "", label: `${p.baseUnitCode} (base unit)` },
      ...p.units.map((u) => ({ value: u.unitId, label: `${u.unitCode} — ${u.unitName} (× ${formatQty(u.conversionFactor)} ${p.baseUnitCode})` })),
    ];
  }, [product.data]);

  const selectedAlt = product.data?.units.find((u) => u.unitId === form.unitId);
  const factor = selectedAlt?.conversionFactor ?? 1;
  const selectedUnitId = form.unitId || product.data?.baseUnitId;
  const allowsFractions = units.data?.find((u) => u.id === selectedUnitId)?.allowsFractions ?? true;

  const qty = parseNumber(form.quantity);

  const buildRequest = (): { path: string; body: unknown } | null => {
    if (!action || !config || !form.product) return null;
    const base = { warehouseId: form.warehouseId, productId: form.product.id };
    const unitId = form.unitId || null;
    switch (action) {
      case "stockIn":
        return {
          path: config.path,
          body: { ...base, quantity: qty, unitId, unitCost: form.unitCost.trim() ? parseNumber(form.unitCost) : null, referenceNumber: nullable(form.referenceNumber), notes: nullable(form.text) },
        };
      case "stockOut":
        return { path: config.path, body: { ...base, quantity: qty, unitId, referenceNumber: nullable(form.referenceNumber), reason: nullable(form.text) } };
      case "damaged":
        return { path: config.path, body: { ...base, quantity: qty, unitId, reason: form.text.trim() } };
      case "return":
        return {
          path: config.path,
          body: { ...base, quantity: qty, unitId, unitCost: form.unitCost.trim() ? parseNumber(form.unitCost) : null, referenceNumber: nullable(form.referenceNumber), reason: form.text.trim() },
        };
      case "count":
        return { path: config.path, body: { ...base, countedQuantity: qty, notes: nullable(form.text) } };
      case "correction":
        return { path: config.path, body: { ...base, reason: form.reason, quantityChange: qty, notes: nullable(form.text) } };
      case "reserve":
        return {
          path: config.path,
          body: { ...base, quantity: qty, referenceNumber: nullable(form.referenceNumber), expiresAt: localDateTimeToIso(form.expiresAt), notes: nullable(form.text) },
        };
    }
  };

  const validate = (): Record<string, string | undefined> => {
    const e: Record<string, string | undefined> = {};
    if (!form.warehouseId) e.warehouseId = "Choose a warehouse.";
    if (!form.product) e.productId = "Choose a product.";
    if (action === "count") e.quantity = quantityError(qty, { allowZero: true, label: "Counted quantity" });
    else if (action === "correction") e.quantity = quantityError(qty, { allowNegative: true, label: "Quantity change" });
    else e.quantity = quantityError(qty);
    if (!e.quantity && !allowsFractions && config?.usesUnits && !Number.isInteger(qty)) e.quantity = "This unit only allows whole numbers.";
    if (!e.quantity && current && (action === "stockOut" || action === "damaged" || action === "reserve") && qty * factor > available) {
      e.quantity = `Only ${formatQty(available)} ${baseUnitCode} available.`;
    }
    if ((action === "stockIn" || action === "return") && form.unitCost.trim()) {
      const cost = parseNumber(form.unitCost);
      if (Number.isNaN(cost) || cost < 0) e.unitCost = "Unit cost must be zero or more.";
    }
    if ((action === "damaged" || action === "return") && !form.text.trim()) e.text = "A reason is required.";
    if (action === "correction" && !form.text.trim()) e.text = "Explain the correction.";
    if (action === "reserve" && form.expiresAt) {
      const iso = localDateTimeToIso(form.expiresAt);
      if (!iso || new Date(iso).getTime() <= Date.now()) e.expiresAt = "Expiry must be in the future.";
    }
    if (form.referenceNumber.length > 64) e.referenceNumber = "At most 64 characters.";
    return e;
  };

  const mutation = useMutation({
    mutationFn: async (): Promise<ApiResult<StockMovement | StockAdjustment | StockReservation>> => {
      const request = buildRequest();
      if (!request) throw new Error("Incomplete form.");
      // Same payload → same key, so a double-click or a network retry is replayed, never double-booked.
      const key = submission.keyFor(request);
      return api.postIdempotent<StockMovement | StockAdjustment | StockReservation>(request.path, request.body, key);
    },
    onSuccess: (result) => {
      submission.reset();
      for (const root of ["inventory", "movements", "dashboard", "products", "adjustments", "reservations"]) {
        void queryClient.invalidateQueries({ queryKey: [root] });
      }
      const name = form.product?.name ?? "product";
      if (result.replayed) {
        toast.info("Already recorded", "This submission was processed earlier — no duplicate was created.");
      } else if (action === "count" || action === "correction") {
        const adj = result.data as StockAdjustment;
        toast.success(
          adj.status === "PendingApproval" ? `Adjustment ${adj.number} submitted for approval` : `Adjustment ${adj.number} applied`,
          `${name}: change of ${formatQty(adj.quantityChange)} ${baseUnitCode}`.trim(),
        );
      } else if (action === "reserve") {
        toast.success("Stock reserved", `${formatQty(qty)} ${baseUnitCode} of ${name} held.`);
      } else {
        const m = result.data as StockMovement;
        toast.success(`${config?.title ?? "Movement"} recorded`, `${name}: on hand now ${formatQty(m.onHandAfter)}.`);
      }
      onClose();
    },
    onError: (err) => {
      if (err instanceof ApiError && err.code === "insufficient_stock") {
        const avail = err.extra<number>("available");
        setErrors({ quantity: avail !== undefined ? `Only ${formatQty(avail)} ${baseUnitCode} available.` : err.detail });
        setFormError(null);
        void balance.refetch();
        return;
      }
      const fe = fieldErrors(err);
      setErrors({
        warehouseId: fe.warehouseId,
        productId: fe.productId,
        quantity: fe.quantity ?? fe.countedQuantity ?? fe.quantityChange,
        unitCost: fe.unitCost,
        referenceNumber: fe.referenceNumber,
        text: fe.reason ?? fe.notes,
        expiresAt: fe.expiresAt,
      });
      setFormError(err instanceof Error ? err.message : "The operation failed.");
      if (!(err instanceof ApiError && err.status === 400)) toast.error(err);
    },
  });

  if (!config || !action) return null;
  const permitted = hasPermission(config.permission);

  const submit = () => {
    const e = validate();
    setErrors(e);
    setFormError(null);
    if (Object.values(e).some(Boolean)) return;
    mutation.mutate();
  };

  const textLabel =
    action === "stockIn" ? "Notes" : action === "stockOut" ? "Reason" : action === "count" || action === "reserve" ? "Notes" : action === "correction" ? "Explanation" : "Reason";
  const textRequired = action === "damaged" || action === "return" || action === "correction";
  const variance = action === "count" && current && !Number.isNaN(qty) ? qty - onHand : null;

  return (
    <Dialog
      open={open}
      onClose={onClose}
      busy={mutation.isPending}
      title={config.title}
      description={config.description}
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
          <Button type="submit" loading={mutation.isPending} disabled={!permitted} variant={action === "damaged" ? "danger" : "primary"}>
            {config.submitLabel}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {!permitted && <FormError message="You do not have permission for this operation." />}
        <div className="grid gap-4 sm:grid-cols-2">
          <Select
            label="Warehouse"
            required
            value={form.warehouseId}
            onChange={(e) => set("warehouseId", e.target.value)}
            disabled={!!preset}
            error={errors.warehouseId}
            placeholder="Select warehouse…"
            options={(warehouses.data ?? []).map((w) => ({ value: w.id, label: `${w.code} — ${w.name}` }))}
          />
          <ProductPicker
            value={form.product}
            onChange={(p) => {
              set("product", p);
              set("unitId", "");
            }}
            disabled={!!preset}
            required
            error={errors.productId}
          />
        </div>

        {form.warehouseId && form.product && (
          <div className="grid grid-cols-3 gap-3 rounded-md border border-slate-200 bg-slate-50 px-3 py-2.5 text-center text-xs">
            {balance.isPending ? (
              <p className="col-span-3 text-slate-500">Loading current stock…</p>
            ) : (
              <>
                <div>
                  <p className="text-slate-500">On hand</p>
                  <p className="mt-0.5 text-sm font-semibold tabular-nums text-slate-900">{formatQty(onHand)}</p>
                </div>
                <div>
                  <p className="text-slate-500">Reserved</p>
                  <p className="mt-0.5 text-sm font-semibold tabular-nums text-slate-900">{formatQty(current?.quantityReserved ?? 0)}</p>
                </div>
                <div>
                  <p className="text-slate-500">Available</p>
                  <p className="mt-0.5 text-sm font-semibold tabular-nums text-slate-900">
                    {formatQty(available)} <span className="font-normal text-slate-500">{baseUnitCode}</span>
                  </p>
                </div>
              </>
            )}
          </div>
        )}

        {action === "correction" && (
          <Select
            label="Reason"
            required
            value={form.reason}
            onChange={(e) => set("reason", e.target.value as FormState["reason"])}
            options={CORRECTION_REASONS}
          />
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label={action === "count" ? "Counted quantity" : action === "correction" ? "Quantity change" : "Quantity"}
            required
            type="number"
            inputMode="decimal"
            step="any"
            min={action === "correction" ? undefined : 0}
            value={form.quantity}
            onChange={(e) => set("quantity", e.target.value)}
            error={errors.quantity}
            hint={
              action === "correction"
                ? "Positive adds stock, negative removes it."
                : variance !== null
                  ? `Variance vs. system: ${variance > 0 ? "+" : ""}${formatQty(variance)} ${baseUnitCode}`
                  : config.usesUnits && selectedAlt && !Number.isNaN(qty)
                    ? `= ${formatQty(qty * factor)} ${baseUnitCode}`
                    : undefined
            }
            data-autofocus={preset ? true : undefined}
          />
          {config.usesUnits && (
            <Select
              label="Unit"
              value={form.unitId}
              onChange={(e) => set("unitId", e.target.value)}
              disabled={!product.data}
              options={unitOptions.length ? unitOptions : [{ value: "", label: baseUnitCode ? `${baseUnitCode} (base unit)` : "Base unit" }]}
            />
          )}
          {action === "reserve" && (
            <Input
              label="Expires at"
              type="datetime-local"
              value={form.expiresAt}
              onChange={(e) => set("expiresAt", e.target.value)}
              error={errors.expiresAt}
              hint="Optional. The hold is released automatically after this time."
            />
          )}
        </div>

        {(action === "stockIn" || action === "return") && (
          <Input
            label="Unit cost"
            type="number"
            inputMode="decimal"
            step="any"
            min={0}
            value={form.unitCost}
            onChange={(e) => set("unitCost", e.target.value)}
            error={errors.unitCost}
            hint={selectedAlt ? `Per ${selectedAlt.unitCode}. Leave blank to use the current average cost.` : "Leave blank to use the current average cost."}
          />
        )}

        {(action === "stockIn" || action === "stockOut" || action === "return" || action === "reserve") && (
          <Input
            label="Reference number"
            value={form.referenceNumber}
            onChange={(e) => set("referenceNumber", e.target.value)}
            error={errors.referenceNumber}
            maxLength={64}
            placeholder="e.g. PO, delivery note or order number"
          />
        )}

        <Textarea
          label={textLabel}
          required={textRequired}
          value={form.text}
          onChange={(e) => set("text", e.target.value)}
          error={errors.text}
          maxLength={action === "count" || action === "correction" ? 1000 : 500}
        />
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}
