import { Badge, type BadgeTone } from "@/components/ui/Badge";
import { humanize } from "@/lib/format";
import type { AdjustmentStatus, OutboxStatus, ReservationStatus, StockMovementType, TenantStatus, TransferStatus } from "@/lib/types";

const transferTones: Record<TransferStatus, BadgeTone> = {
  Requested: "warning",
  InTransit: "info",
  Completed: "success",
  Cancelled: "neutral",
};

export function TransferStatusBadge({ status }: { status: TransferStatus }) {
  return (
    <Badge tone={transferTones[status]} dot>
      {status === "InTransit" ? "In transit" : humanize(status)}
    </Badge>
  );
}

const adjustmentTones: Record<AdjustmentStatus, BadgeTone> = {
  PendingApproval: "warning",
  Applied: "success",
  Rejected: "danger",
};

export function AdjustmentStatusBadge({ status }: { status: AdjustmentStatus }) {
  return (
    <Badge tone={adjustmentTones[status]} dot>
      {status === "PendingApproval" ? "Pending approval" : humanize(status)}
    </Badge>
  );
}

const reservationTones: Record<ReservationStatus, BadgeTone> = {
  Active: "info",
  Released: "neutral",
  Consumed: "success",
};

export function ReservationStatusBadge({ status }: { status: ReservationStatus }) {
  return (
    <Badge tone={reservationTones[status]} dot>
      {humanize(status)}
    </Badge>
  );
}

const movementTones: Record<StockMovementType, BadgeTone> = {
  StockIn: "success",
  StockOut: "brand",
  TransferOut: "info",
  TransferIn: "info",
  Adjustment: "warning",
  Reservation: "neutral",
  Release: "neutral",
  Damaged: "danger",
  Returned: "success",
};

export const MOVEMENT_LABELS: Record<StockMovementType, string> = {
  StockIn: "Stock in",
  StockOut: "Stock out",
  TransferOut: "Transfer out",
  TransferIn: "Transfer in",
  Adjustment: "Adjustment",
  Reservation: "Reservation",
  Release: "Release",
  Damaged: "Damaged",
  Returned: "Returned",
};

export function MovementTypeBadge({ type }: { type: StockMovementType }) {
  return <Badge tone={movementTones[type]}>{MOVEMENT_LABELS[type] ?? humanize(type)}</Badge>;
}

const jobTones: Record<OutboxStatus, BadgeTone> = {
  Pending: "warning",
  Processing: "info",
  Succeeded: "success",
  Failed: "danger",
};

export function JobStatusBadge({ status }: { status: OutboxStatus }) {
  return (
    <Badge tone={jobTones[status]} dot>
      {status}
    </Badge>
  );
}

export function TenantStatusBadge({ status }: { status: TenantStatus }) {
  return (
    <Badge tone={status === "Active" ? "success" : "danger"} dot>
      {status}
    </Badge>
  );
}

export function ActiveBadge({ active, activeLabel = "Active", inactiveLabel = "Inactive" }: { active: boolean; activeLabel?: string; inactiveLabel?: string }) {
  return (
    <Badge tone={active ? "success" : "neutral"} dot>
      {active ? activeLabel : inactiveLabel}
    </Badge>
  );
}

/** Signed, coloured quantity (green inbound, red outbound). */
export function SignedQty({ value, className }: { value: number; className?: string }) {
  const tone = value > 0 ? "text-emerald-700" : value < 0 ? "text-red-600" : "text-slate-500";
  const abs = new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(Math.abs(value));
  return (
    <span className={`font-medium tabular-nums ${tone} ${className ?? ""}`}>
      {value > 0 ? "+" : value < 0 ? "−" : ""}
      {abs}
    </span>
  );
}
