"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowRight, Plus, Trash2 } from "lucide-react";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { controlClasses, FormError } from "@/components/ui/Field";
import { Select } from "@/components/ui/Select";
import { Textarea } from "@/components/ui/Textarea";
import { useToast } from "@/components/ui/Toast";
import { ProductPicker, type PickedProduct } from "@/features/common/ProductPicker";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { cn } from "@/lib/cn";
import { formatQty, parseNumber } from "@/lib/format";
import { useIdempotentSubmission } from "@/lib/hooks";
import { qk, useAccessibleWarehouses, useWarehouses } from "@/lib/queries";
import type { CreateTransferRequest, InventoryBalance, PagedResult, StockTransfer } from "@/lib/types";
import { quantityError } from "@/lib/validation";

interface Line {
  key: number;
  product: PickedProduct | null;
  quantity: string;
}

let lineSeq = 0;
const newLine = (): Line => ({ key: ++lineSeq, product: null, quantity: "" });

function AvailableAt({ warehouseId, productId }: { warehouseId: string; productId: string }) {
  const params = { warehouseId, productId, pageSize: 1 };
  const q = useQuery({
    queryKey: qk.inventory({ ...params, single: true }),
    queryFn: () => api.get<PagedResult<InventoryBalance>>("/api/inventory", params),
    enabled: !!warehouseId && !!productId,
  });
  if (!warehouseId || !productId) return null;
  if (q.isPending) return <span className="text-xs text-slate-400">…</span>;
  const item = q.data?.items[0];
  return (
    <span className={cn("text-xs tabular-nums", item && item.quantityAvailable > 0 ? "text-slate-500" : "text-red-600")}>
      {formatQty(item?.quantityAvailable ?? 0)} {item?.unitCode ?? ""} available
    </span>
  );
}

export function NewTransferDialog({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated?: (t: StockTransfer) => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const sources = useAccessibleWarehouses();
  const destinations = useWarehouses();
  const submission = useIdempotentSubmission();
  const [source, setSource] = useState("");
  const [destination, setDestination] = useState("");
  const [lines, setLines] = useState<Line[]>([newLine()]);
  const [notes, setNotes] = useState("");
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setSource(sources.data?.length === 1 ? sources.data[0].id : "");
      setDestination("");
      setLines([newLine()]);
      setNotes("");
      setErrors({});
      setFormError(null);
      submission.reset();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const updateLine = (key: number, patch: Partial<Line>) => {
    setLines((ls) => ls.map((l) => (l.key === key ? { ...l, ...patch } : l)));
  };

  const mutation = useMutation({
    mutationFn: async () => {
      const body: CreateTransferRequest = {
        sourceWarehouseId: source,
        destinationWarehouseId: destination,
        lines: lines.filter((l) => l.product).map((l) => ({ productId: l.product!.id, quantity: parseNumber(l.quantity) })),
        notes: notes.trim() || null,
      };
      return api.postIdempotent<StockTransfer>("/api/stock-transfers", body, submission.keyFor(body));
    },
    onSuccess: (result) => {
      submission.reset();
      for (const root of ["transfers", "inventory", "reservations", "dashboard", "movements"]) void queryClient.invalidateQueries({ queryKey: [root] });
      if (result.replayed) toast.info("Already submitted", `Transfer ${result.data.number} was created earlier — no duplicate was made.`);
      else toast.success(`Transfer ${result.data.number} requested`, "Stock is reserved at the source until dispatch.");
      onCreated?.(result.data);
      onClose();
    },
    onError: (err) => {
      if (err instanceof ApiError && err.code === "insufficient_stock") {
        const productId = err.extra<string>("productId");
        const available = err.extra<number>("available");
        const line = lines.find((l) => l.product?.id === productId);
        if (line) {
          setErrors({ [`line-${line.key}`]: `Only ${formatQty(available ?? 0)} available at the source.` });
          setFormError(null);
          return;
        }
      }
      const fe = fieldErrors(err);
      const mapped: Record<string, string | undefined> = {
        source: fe.sourceWarehouseId,
        destination: fe.destinationWarehouseId,
        lines: fe.lines,
        notes: fe.notes,
      };
      lines.forEach((l, i) => {
        const msg = fe[`lines[${i}].quantity`] ?? fe[`lines[${i}].productId`];
        if (msg) mapped[`line-${l.key}`] = msg;
      });
      setErrors(mapped);
      setFormError(err instanceof Error ? err.message : "Could not create the transfer.");
      if (!(err instanceof ApiError && err.status === 400)) toast.error(err);
    },
  });

  const submit = () => {
    const e: Record<string, string | undefined> = {};
    if (!source) e.source = "Choose the source warehouse.";
    if (!destination) e.destination = "Choose the destination warehouse.";
    if (source && destination && source === destination) e.destination = "Source and destination must differ.";
    const filled = lines.filter((l) => l.product || l.quantity);
    if (filled.length === 0) e.lines = "Add at least one product.";
    if (filled.length > 200) e.lines = "A transfer can have at most 200 lines.";
    const seen = new Set<string>();
    for (const l of filled) {
      if (!l.product) e[`line-${l.key}`] = "Choose a product.";
      else if (seen.has(l.product.id)) e[`line-${l.key}`] = "Each product may appear only once.";
      else {
        seen.add(l.product.id);
        const qe = quantityError(parseNumber(l.quantity));
        if (qe) e[`line-${l.key}`] = qe;
      }
    }
    setErrors(e);
    setFormError(null);
    if (Object.values(e).some(Boolean)) return;
    setLines(filled);
    mutation.mutate();
  };

  const destinationOptions = (destinations.data ?? []).filter((w) => w.id !== source).map((w) => ({ value: w.id, label: `${w.code} — ${w.name}` }));

  return (
    <Dialog
      open={open}
      onClose={onClose}
      busy={mutation.isPending}
      size="lg"
      title="New stock transfer"
      description="Move stock between warehouses. Quantities are reserved at the source until the transfer is dispatched."
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
          <Button type="submit" loading={mutation.isPending}>
            Request transfer
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        <div className="grid items-start gap-3 sm:grid-cols-[1fr_auto_1fr]">
          <Select
            label="From"
            required
            value={source}
            onChange={(e) => {
              setSource(e.target.value);
              if (e.target.value === destination) setDestination("");
            }}
            error={errors.source}
            placeholder="Source warehouse…"
            options={(sources.data ?? []).map((w) => ({ value: w.id, label: `${w.code} — ${w.name}` }))}
          />
          <ArrowRight className="mx-auto mt-8 hidden size-4 text-slate-400 sm:block" aria-hidden />
          <Select
            label="To"
            required
            value={destination}
            onChange={(e) => setDestination(e.target.value)}
            error={errors.destination}
            placeholder="Destination warehouse…"
            options={destinationOptions}
          />
        </div>

        <fieldset>
          <legend className="mb-2 text-sm font-semibold text-slate-900">Products</legend>
          {errors.lines && <p className="mb-2 text-xs font-medium text-red-600">{errors.lines}</p>}
          <ul className="space-y-3">
            {lines.map((line, index) => (
              <li key={line.key} className="rounded-md border border-slate-200 p-3">
                <div className="grid items-start gap-3 sm:grid-cols-[1fr_9rem_auto]">
                  <ProductPicker
                    label={`Product ${index + 1}`}
                    value={line.product}
                    onChange={(p) => updateLine(line.key, { product: p })}
                    excludeIds={lines.filter((l) => l.key !== line.key && l.product).map((l) => l.product!.id)}
                  />
                  <label className="flex flex-col gap-1.5">
                    <span className="text-sm font-medium text-slate-700">Quantity</span>
                    <input
                      type="number"
                      inputMode="decimal"
                      step="any"
                      min={0}
                      value={line.quantity}
                      onChange={(e) => updateLine(line.key, { quantity: e.target.value })}
                      aria-invalid={errors[`line-${line.key}`] ? true : undefined}
                      className={cn(controlClasses, "h-9")}
                    />
                  </label>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="mt-6"
                    onClick={() => setLines((ls) => (ls.length > 1 ? ls.filter((l) => l.key !== line.key) : [newLine()]))}
                    aria-label={`Remove product line ${index + 1}`}
                  >
                    <Trash2 className="size-4" />
                  </Button>
                </div>
                <div className="mt-1.5 flex flex-wrap items-center justify-between gap-2">
                  {line.product && source ? <AvailableAt warehouseId={source} productId={line.product.id} /> : <span />}
                  {errors[`line-${line.key}`] && <p className="text-xs font-medium text-red-600">{errors[`line-${line.key}`]}</p>}
                </div>
              </li>
            ))}
          </ul>
          <Button variant="secondary" size="sm" className="mt-3" icon={<Plus className="size-3.5" />} onClick={() => setLines((ls) => [...ls, newLine()])} disabled={lines.length >= 200}>
            Add product
          </Button>
        </fieldset>

        <Textarea label="Notes" value={notes} onChange={(e) => setNotes(e.target.value)} error={errors.notes} maxLength={1000} />
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}
