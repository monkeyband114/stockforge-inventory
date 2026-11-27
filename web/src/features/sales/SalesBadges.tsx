import { Badge, type BadgeTone } from "@/components/ui/Badge";
import type { InvoiceStatus, PaymentMethod, SalesOrderStatus, ShipmentStatus } from "@/lib/types";

export const ORDER_STATUS_LABELS: Record<SalesOrderStatus, string> = {
  Draft: "Draft",
  PendingApproval: "Pending approval",
  Approved: "Approved",
  Processing: "Processing",
  Picking: "Picking",
  Packed: "Packed",
  Shipped: "Shipped",
  Delivered: "Delivered",
  Cancelled: "Cancelled",
  Rejected: "Rejected",
  PartiallyFulfilled: "Partially fulfilled",
  Returned: "Returned",
};

const orderTones: Record<SalesOrderStatus, BadgeTone> = {
  Draft: "neutral",
  PendingApproval: "warning",
  Approved: "info",
  Processing: "info",
  Picking: "info",
  Packed: "brand",
  Shipped: "success",
  Delivered: "success",
  Cancelled: "neutral",
  Rejected: "danger",
  PartiallyFulfilled: "warning",
  Returned: "neutral",
};

export function OrderStatusBadge({ status }: { status: SalesOrderStatus | string }) {
  const s = status as SalesOrderStatus;
  return (
    <Badge tone={orderTones[s] ?? "neutral"} dot>
      {ORDER_STATUS_LABELS[s] ?? status}
    </Badge>
  );
}

export const INVOICE_STATUS_LABELS: Record<InvoiceStatus, string> = {
  Draft: "Draft",
  Issued: "Issued",
  PartiallyPaid: "Partially paid",
  Paid: "Paid",
  Overdue: "Overdue",
  Cancelled: "Cancelled",
};

const invoiceTones: Record<InvoiceStatus, BadgeTone> = {
  Draft: "neutral",
  Issued: "info",
  PartiallyPaid: "warning",
  Paid: "success",
  Overdue: "danger",
  Cancelled: "neutral",
};

export function InvoiceStatusBadge({ status }: { status: InvoiceStatus }) {
  return (
    <Badge tone={invoiceTones[status] ?? "neutral"} dot>
      {INVOICE_STATUS_LABELS[status] ?? status}
    </Badge>
  );
}

export function ShipmentStatusBadge({ status }: { status: ShipmentStatus }) {
  return (
    <Badge tone={status === "Delivered" ? "success" : "info"} dot>
      {status === "Delivered" ? "Delivered" : "In transit"}
    </Badge>
  );
}

export const PAYMENT_METHOD_LABELS: Record<PaymentMethod, string> = {
  BankTransfer: "Bank transfer",
  Card: "Card",
  Cash: "Cash",
  Cheque: "Cheque",
  Other: "Other",
};

/** Statuses whose stock is still being worked by the warehouse. */
export const OPEN_ORDER_STATUSES: SalesOrderStatus[] = ["Approved", "Processing", "Picking", "Packed", "PartiallyFulfilled"];
