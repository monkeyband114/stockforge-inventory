// In-memory hand-off from the reorder-suggestions page to the new-PO form (client navigation only;
// nothing is persisted). A full reload simply starts with an empty form.
import type { PickedProduct } from "@/features/common/ProductPicker";
import type { PickedSupplier } from "./SupplierPicker";

export interface PoPrefill {
  supplier: PickedSupplier | null;
  warehouseId: string;
  lines: { product: PickedProduct; quantity: number; unitCost: number | null }[];
}

let pending: PoPrefill | null = null;

export function setPoPrefill(prefill: PoPrefill): void {
  pending = prefill;
}

/** Non-destructive read (safe inside state initializers, which StrictMode may run twice). */
export function peekPoPrefill(): PoPrefill | null {
  return pending;
}

/** Call from an effect once the form has consumed the prefill. */
export function clearPoPrefill(): void {
  pending = null;
}
