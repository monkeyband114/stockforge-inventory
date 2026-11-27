import { Ban, CheckCircle2, Circle, FilePen, PackageCheck, Send, Truck, XCircle } from "lucide-react";
import { cn } from "@/lib/cn";
import { formatDateTime } from "@/lib/format";
import type { Order } from "@/lib/types";

interface Step {
  label: string;
  at: string | null;
  detail?: string | null;
  icon: typeof Circle;
  tone: "done" | "pending" | "danger";
}

export function OrderTimeline({ order }: { order: Order }) {
  const steps: Step[] = [{ label: "Created", at: order.createdAt, detail: order.createdByName, icon: FilePen, tone: "done" }];
  steps.push({ label: "Submitted", at: order.submittedAt, icon: Send, tone: order.submittedAt ? "done" : "pending" });
  if (order.rejectedAt) {
    steps.push({ label: "Rejected", at: order.rejectedAt, detail: order.rejectionReason, icon: XCircle, tone: "danger" });
  } else {
    steps.push({
      label: order.requiresApproval ? "Approved" : "Approved (automatic)",
      at: order.approvedAt,
      detail: order.approvedByName,
      icon: CheckCircle2,
      tone: order.approvedAt ? "done" : "pending",
    });
  }
  if (order.cancelledAt) {
    steps.push({ label: "Cancelled", at: order.cancelledAt, detail: order.cancellationReason, icon: Ban, tone: "danger" });
  } else if (!order.rejectedAt) {
    steps.push({ label: order.status === "PartiallyFulfilled" ? "Partially shipped" : "Shipped", at: order.shippedAt, icon: Truck, tone: order.shippedAt ? "done" : "pending" });
    steps.push({ label: "Delivered", at: order.deliveredAt, icon: PackageCheck, tone: order.deliveredAt ? "done" : "pending" });
  }

  return (
    <ol className="space-y-4" aria-label="Order timeline">
      {steps.map((s, i) => {
        const Icon = s.icon;
        return (
          <li key={s.label} className="relative flex gap-3">
            {i < steps.length - 1 && (
              <span className={cn("absolute left-[11px] top-6 h-[calc(100%-4px)] w-px", s.tone === "done" ? "bg-brand-600/40" : "bg-slate-200")} aria-hidden="true" />
            )}
            <span
              className={cn(
                "relative z-10 flex size-6 shrink-0 items-center justify-center rounded-full",
                s.tone === "danger" ? "bg-red-50 text-red-600" : s.tone === "done" ? "bg-brand-50 text-brand-700" : "bg-slate-100 text-slate-400",
              )}
            >
              <Icon className="size-3.5" aria-hidden />
            </span>
            <div className="min-w-0">
              <p className={cn("text-sm font-medium", s.tone === "pending" ? "text-slate-400" : "text-slate-900")}>
                {s.label}
                <span className="sr-only">{s.tone === "pending" ? " (pending)" : ""}</span>
              </p>
              <p className="text-xs text-slate-500">
                {s.at ? formatDateTime(s.at) : "Pending"}
                {s.detail && s.tone !== "danger" ? ` · ${s.detail}` : ""}
              </p>
              {s.detail && s.tone === "danger" && <p className="mt-0.5 text-xs text-red-700">“{s.detail}”</p>}
            </div>
          </li>
        );
      })}
    </ol>
  );
}
