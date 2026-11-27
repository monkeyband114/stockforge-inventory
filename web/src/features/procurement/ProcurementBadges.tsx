import { CheckCircle2, CircleAlert } from "lucide-react";
import { Badge, type BadgeTone } from "@/components/ui/Badge";
import type { PurchaseOrderStatus, SupplierInvoiceStatus } from "@/lib/types";

export const PO_STATUS_LABELS: Record<PurchaseOrderStatus, string> = {
  Draft: "Draft",
  PendingApproval: "Pending approval",
  Approved: "Approved",
  PartiallyReceived: "Partially received",
  Received: "Received",
  Closed: "Closed short",
  Cancelled: "Cancelled",
  Rejected: "Rejected",
};

const poTones: Record<PurchaseOrderStatus, BadgeTone> = {
  Draft: "neutral",
  PendingApproval: "warning",
  Approved: "info",
  PartiallyReceived: "warning",
  Received: "success",
  Closed: "neutral",
  Cancelled: "neutral",
  Rejected: "danger",
};

export function PurchaseOrderStatusBadge({ status }: { status: PurchaseOrderStatus }) {
  return (
    <Badge tone={poTones[status] ?? "neutral"} dot>
      {PO_STATUS_LABELS[status] ?? status}
    </Badge>
  );
}

export const BILL_STATUS_LABELS: Record<SupplierInvoiceStatus, string> = {
  Open: "Open",
  PartiallyPaid: "Partially paid",
  Paid: "Paid",
  Overdue: "Overdue",
  Cancelled: "Cancelled",
};

const billTones: Record<SupplierInvoiceStatus, BadgeTone> = {
  Open: "info",
  PartiallyPaid: "warning",
  Paid: "success",
  Overdue: "danger",
  Cancelled: "neutral",
};

export function SupplierInvoiceStatusBadge({ status }: { status: SupplierInvoiceStatus }) {
  return (
    <Badge tone={billTones[status] ?? "neutral"} dot>
      {BILL_STATUS_LABELS[status] ?? status}
    </Badge>
  );
}

/** Three-way match result (bill ≤ value of goods received). Icon + text, never colour alone. */
export function MatchBadge({ matches, hasPo }: { matches: boolean; hasPo: boolean }) {
  if (!hasPo) return <Badge tone="neutral">No PO</Badge>;
  return matches ? (
    <Badge tone="success">
      <CheckCircle2 className="size-3" aria-hidden /> Matched
    </Badge>
  ) : (
    <Badge tone="danger">
      <CircleAlert className="size-3" aria-hidden /> Exceeds receipts
    </Badge>
  );
}

/** Statuses in which goods can still be received. */
export const RECEIVABLE_PO_STATUSES: PurchaseOrderStatus[] = ["Approved", "PartiallyReceived"];
