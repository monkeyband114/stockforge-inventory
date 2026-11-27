"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { controlClasses, FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { Textarea } from "@/components/ui/Textarea";
import { ApiError } from "@/lib/api";
import { cn } from "@/lib/cn";
import { formatQty, parseNumber } from "@/lib/format";
import type { CreateReturnRequest, Order, RecordPickRequest, ShipOrderRequest } from "@/lib/types";

function errorText(err: unknown): string {
  return err instanceof ApiError ? err.detail : err instanceof Error ? err.message : "The action failed.";
}

const qtyInputClass = cn(controlClasses, "h-8 w-24 text-right tabular-nums");

/** Record picked quantities per line (defaults to what is still to pick). */
export function PickDialog({ order, open, onClose, onSubmit }: { order: Order; open: boolean; onClose: () => void; onSubmit: (body: RecordPickRequest) => Promise<unknown> }) {
  const [qty, setQty] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const remaining = (l: Order["lines"][number]) => Math.max(0, l.quantity - l.quantityPicked);

  useEffect(() => {
    if (open) {
      setQty(Object.fromEntries(order.lines.map((l) => [l.id, remaining(l) > 0 ? String(remaining(l)) : ""])));
      setErrors({});
      setFormError(null);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, order]);

  const submit = async () => {
    const e: Record<string, string> = {};
    const lines: RecordPickRequest["lines"] = [];
    for (const l of order.lines) {
      const raw = qty[l.id]?.trim() ?? "";
      if (!raw) continue;
      const q = parseNumber(raw);
      if (Number.isNaN(q) || q < 0) e[l.id] = "Invalid quantity.";
      else if (q > remaining(l)) e[l.id] = `Only ${formatQty(remaining(l))} left to pick.`;
      else if (q > 0) lines.push({ lineId: l.id, quantity: q });
    }
    setErrors(e);
    if (Object.keys(e).length) return;
    if (lines.length === 0) {
      setFormError("Enter a picked quantity for at least one line.");
      return;
    }
    setBusy(true);
    setFormError(null);
    try {
      await onSubmit({ lines });
      onClose();
    } catch (err) {
      setFormError(errorText(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      busy={busy}
      size="lg"
      title={`Record pick — ${order.number}`}
      description="Enter the quantities taken from the shelves. You can record picks in several passes."
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            Record pick
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <div className="overflow-hidden rounded-lg border border-slate-200">
          <Table>
            <THead>
              <tr>
                <TH>Product</TH>
                <TH align="right">Ordered</TH>
                <TH align="right">Picked</TH>
                <TH align="right">Pick now</TH>
              </tr>
            </THead>
            <TBody>
              {order.lines.map((l) => (
                <TR key={l.id}>
                  <TD>
                    <div className="font-medium text-slate-900">{l.productName}</div>
                    <div className="font-mono text-xs text-slate-500">{l.sku}</div>
                  </TD>
                  <TD numeric>{formatQty(l.quantity)}</TD>
                  <TD numeric>{formatQty(l.quantityPicked)}</TD>
                  <TD align="right">
                    <input
                      type="number"
                      inputMode="decimal"
                      min={0}
                      max={remaining(l)}
                      step="any"
                      aria-label={`Quantity to pick for ${l.productName}`}
                      aria-invalid={errors[l.id] ? true : undefined}
                      disabled={remaining(l) === 0}
                      value={qty[l.id] ?? ""}
                      onChange={(e) => setQty((q) => ({ ...q, [l.id]: e.target.value }))}
                      className={qtyInputClass}
                    />
                    {errors[l.id] && <p className="mt-1 text-xs font-medium text-red-600">{errors[l.id]}</p>}
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        </div>
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}

/** Ship everything packed: carrier, tracking and package count. */
export function ShipDialog({ order, open, onClose, onSubmit }: { order: Order; open: boolean; onClose: () => void; onSubmit: (body: ShipOrderRequest) => Promise<unknown> }) {
  const [carrier, setCarrier] = useState("");
  const [tracking, setTracking] = useState("");
  const [packages, setPackages] = useState("1");
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const toShip = order.lines.reduce((s, l) => s + Math.max(0, l.quantityPacked - l.quantityShipped), 0);

  useEffect(() => {
    if (open) {
      setCarrier("");
      setTracking("");
      setPackages("1");
      setErrors({});
      setFormError(null);
    }
  }, [open]);

  const submit = async () => {
    const count = parseNumber(packages);
    const e = {
      packages: !Number.isInteger(count) || count < 1 || count > 10_000 ? "Between 1 and 10,000 packages." : undefined,
      carrier: carrier.length > 100 ? "At most 100 characters." : undefined,
      tracking: tracking.length > 100 ? "At most 100 characters." : undefined,
    };
    setErrors(e);
    if (Object.values(e).some(Boolean)) return;
    setBusy(true);
    setFormError(null);
    try {
      await onSubmit({ carrier: carrier.trim() || null, trackingNumber: tracking.trim() || null, packageCount: count });
      onClose();
    } catch (err) {
      setFormError(errorText(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      busy={busy}
      title={`Ship ${order.number}`}
      description={`${formatQty(toShip)} packed unit(s) leave ${order.warehouseCode} and stock is issued against the reservation.`}
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            Ship order
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid gap-3 sm:grid-cols-2">
          <Input label="Carrier" value={carrier} onChange={(e) => setCarrier(e.target.value)} error={errors.carrier} maxLength={100} placeholder="e.g. UPS" />
          <Input label="Tracking number" value={tracking} onChange={(e) => setTracking(e.target.value)} error={errors.tracking} maxLength={100} />
        </div>
        <Input label="Packages" type="number" min={1} step={1} required value={packages} onChange={(e) => setPackages(e.target.value)} error={errors.packages} />
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}

/** Receive returned goods: per-line quantity ≤ shipped − returned, restock flag, and a reason. */
export function ReturnDialog({ order, open, onClose, onSubmit }: { order: Order; open: boolean; onClose: () => void; onSubmit: (body: CreateReturnRequest) => Promise<unknown> }) {
  const returnable = order.lines.filter((l) => l.quantityShipped - l.quantityReturned > 0);
  const [qty, setQty] = useState<Record<string, string>>({});
  const [restock, setRestock] = useState<Record<string, boolean>>({});
  const [reason, setReason] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (open) {
      setQty({});
      setRestock(Object.fromEntries(order.lines.map((l) => [l.id, true])));
      setReason("");
      setErrors({});
      setFormError(null);
    }
  }, [open, order]);

  const submit = async () => {
    const e: Record<string, string> = {};
    const lines: CreateReturnRequest["lines"] = [];
    for (const l of returnable) {
      const raw = qty[l.id]?.trim() ?? "";
      if (!raw) continue;
      const q = parseNumber(raw);
      const max = l.quantityShipped - l.quantityReturned;
      if (Number.isNaN(q) || q < 0) e[l.id] = "Invalid quantity.";
      else if (q > max) e[l.id] = `At most ${formatQty(max)} can be returned.`;
      else if (q > 0) lines.push({ lineId: l.id, quantity: q, restock: restock[l.id] ?? true });
    }
    if (!reason.trim()) e.reason = "A reason is required.";
    setErrors(e);
    if (Object.keys(e).length) return;
    if (lines.length === 0) {
      setFormError("Enter a returned quantity for at least one line.");
      return;
    }
    setBusy(true);
    setFormError(null);
    try {
      await onSubmit({ lines, reason: reason.trim() });
      onClose();
    } catch (err) {
      setFormError(errorText(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      busy={busy}
      size="lg"
      title={`Create return — ${order.number}`}
      description="Restocked units go back into the warehouse; the value of every returned unit is credited on the invoice."
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button type="submit" loading={busy}>
            Record return
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {returnable.length === 0 ? (
          <p className="text-sm text-slate-500">Nothing on this order can be returned.</p>
        ) : (
          <div className="overflow-hidden rounded-lg border border-slate-200">
            <Table>
              <THead>
                <tr>
                  <TH>Product</TH>
                  <TH align="right">Shipped</TH>
                  <TH align="right">Returned</TH>
                  <TH align="right">Return now</TH>
                  <TH align="center">Restock</TH>
                </tr>
              </THead>
              <TBody>
                {returnable.map((l) => (
                  <TR key={l.id}>
                    <TD>
                      <div className="font-medium text-slate-900">{l.productName}</div>
                      <div className="font-mono text-xs text-slate-500">{l.sku}</div>
                    </TD>
                    <TD numeric>{formatQty(l.quantityShipped)}</TD>
                    <TD numeric>{formatQty(l.quantityReturned)}</TD>
                    <TD align="right">
                      <input
                        type="number"
                        inputMode="decimal"
                        min={0}
                        max={l.quantityShipped - l.quantityReturned}
                        step="any"
                        aria-label={`Quantity returned for ${l.productName}`}
                        aria-invalid={errors[l.id] ? true : undefined}
                        value={qty[l.id] ?? ""}
                        onChange={(e) => setQty((q) => ({ ...q, [l.id]: e.target.value }))}
                        className={qtyInputClass}
                      />
                      {errors[l.id] && <p className="mt-1 text-xs font-medium text-red-600">{errors[l.id]}</p>}
                    </TD>
                    <TD align="center">
                      <input
                        type="checkbox"
                        aria-label={`Restock ${l.productName}`}
                        checked={restock[l.id] ?? true}
                        onChange={(e) => setRestock((r) => ({ ...r, [l.id]: e.target.checked }))}
                        className="size-4 accent-brand-700"
                      />
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          </div>
        )}
        <Textarea label="Reason" required value={reason} onChange={(e) => setReason(e.target.value)} error={errors.reason} maxLength={1000} />
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}
