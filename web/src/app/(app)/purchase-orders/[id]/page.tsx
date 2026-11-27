"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Ban, Check, CheckCircle2, Circle, FilePen, FileText, PackageCheck, Pencil, Send, Truck, X, XCircle } from "lucide-react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState, type ReactNode } from "react";
import { Button, ButtonLink } from "@/components/ui/Button";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { EmptyState } from "@/components/ui/EmptyState";
import { ErrorState } from "@/components/ui/ErrorState";
import { PageHeader } from "@/components/ui/PageHeader";
import { Skeleton } from "@/components/ui/Skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { RequirePermission } from "@/features/common/RequirePermission";
import { MatchBadge, PurchaseOrderStatusBadge, RECEIVABLE_PO_STATUSES, SupplierInvoiceStatusBadge } from "@/features/procurement/ProcurementBadges";
import { ReceiveGoodsDialog, RECEIPT_ROOTS } from "@/features/procurement/ReceiveGoodsDialog";
import { SupplierInvoiceDialog } from "@/features/procurement/SupplierInvoiceDialog";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { formatCurrency, formatDate, formatDateTime, formatPercent, formatQty, fullName } from "@/lib/format";
import { useIdempotentSubmission } from "@/lib/hooks";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import type { PurchaseOrder } from "@/lib/types";

type Action = "submit" | "approve" | "reject" | "cancel" | "close";
const IDEMPOTENT = new Set<Action>(["submit", "approve"]);

const COPY: Record<Action, { title: string; label: string; body: (po: PurchaseOrder) => string; tone?: "danger"; reason?: boolean; done: string }> = {
  submit: { title: "Submit", label: "Submit", body: () => "Orders within the purchase approval threshold are approved automatically; larger ones go to an approver.", done: "submitted" },
  approve: { title: "Approve", label: "Approve", body: (po) => `Commits ${formatCurrency(po.total, po.currency)} with ${po.supplierName}. Goods can then be received at ${po.warehouseCode}.`, done: "approved" },
  reject: { title: "Reject", label: "Reject", body: () => "The purchase order is closed without being sent to the supplier.", tone: "danger", reason: true, done: "rejected" },
  cancel: { title: "Cancel", label: "Cancel purchase order", body: () => "Only possible before anything is received. This cannot be undone.", tone: "danger", done: "cancelled" },
  close: { title: "Close short", label: "Close short", body: () => "The outstanding quantity will not be delivered. Received goods and bills are unaffected.", done: "closed short" },
};

export default function PurchaseOrderDetailPage() {
  return (
    <RequirePermission anyOf={[P.purchasingView]}>
      <PurchaseOrderView />
    </RequirePermission>
  );
}

function Timeline({ po }: { po: PurchaseOrder }) {
  const lastReceipt = [...po.receipts].sort((a, b) => b.receivedAt.localeCompare(a.receivedAt))[0];
  type Step = { label: string; at: string | null; detail?: string | null; icon: typeof Circle; tone: "done" | "pending" | "danger" };
  const steps: Step[] = [
    { label: "Created", at: po.createdAt, detail: po.createdByName, icon: FilePen, tone: "done" },
    { label: "Submitted", at: po.submittedAt, icon: Send, tone: po.submittedAt ? "done" : "pending" },
  ];
  if (po.rejectedAt) steps.push({ label: "Rejected", at: po.rejectedAt, detail: po.rejectionReason, icon: XCircle, tone: "danger" });
  else steps.push({ label: po.requiresApproval ? "Approved" : "Approved (automatic)", at: po.approvedAt, detail: po.approvedByName, icon: CheckCircle2, tone: po.approvedAt ? "done" : "pending" });
  if (po.cancelledAt) steps.push({ label: "Cancelled", at: po.cancelledAt, icon: Ban, tone: "danger" });
  else if (!po.rejectedAt) {
    steps.push({
      label: po.status === "Received" ? "Fully received" : po.receipts.length ? `Partially received (${po.receipts.length} ${po.receipts.length === 1 ? "delivery" : "deliveries"})` : "Received",
      at: lastReceipt?.receivedAt ?? null,
      icon: Truck,
      tone: po.status === "Received" || po.status === "Closed" ? "done" : po.receipts.length ? "done" : "pending",
    });
    if (po.closedAt) steps.push({ label: "Closed short", at: po.closedAt, icon: PackageCheck, tone: "done" });
  }
  return (
    <ol className="space-y-4" aria-label="Purchase order timeline">
      {steps.map((s, i) => {
        const Icon = s.icon;
        return (
          <li key={s.label} className="relative flex gap-3">
            {i < steps.length - 1 && <span className={cn("absolute left-[11px] top-6 h-[calc(100%-4px)] w-px", s.tone === "done" ? "bg-brand-600/40" : "bg-slate-200")} aria-hidden="true" />}
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

function Amount({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className={cn("flex justify-between gap-6", strong && "border-t border-slate-200 pt-2 text-base font-semibold")}>
      <dt className={strong ? "text-slate-900" : "text-slate-500"}>{label}</dt>
      <dd className="tabular-nums text-slate-900">{value}</dd>
    </div>
  );
}

function PurchaseOrderView() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const { user, hasPermission, hasAnyPermission, canAccessWarehouse } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const submission = useIdempotentSubmission();
  const [confirm, setConfirm] = useState<Action | null>(null);
  const [receiving, setReceiving] = useState(false);
  const [billing, setBilling] = useState(false);

  const query = useQuery({ queryKey: qk.purchaseOrder(id), queryFn: () => api.get<PurchaseOrder>(`/api/purchase-orders/${id}`) });
  const po = query.data;

  const action = useMutation({
    mutationFn: async ({ name, body }: { name: Action; body?: unknown }) => {
      const path = `/api/purchase-orders/${id}/${name}`;
      if (IDEMPOTENT.has(name) && po) return (await api.postIdempotent<PurchaseOrder>(path, body ?? {}, submission.keyFor({ id, name, v: po.version }))).data;
      return api.post<PurchaseOrder>(path, body);
    },
    onSuccess: (updated) => {
      submission.reset();
      queryClient.setQueryData(qk.purchaseOrder(updated.id), updated);
      for (const root of RECEIPT_ROOTS) void queryClient.invalidateQueries({ queryKey: [root], refetchType: "active" });
    },
  });

  if (query.isPending) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-72" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }
  if (query.isError || !po) {
    return (
      <Card>
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </Card>
    );
  }

  const s = po.status;
  // The server enforces self_approval regardless; this only hides the button.
  const youCreated = !!user && po.createdByUserId === user.id;
  const canCreate = hasPermission(P.purchasingCreate);
  const canReceive = hasPermission(P.purchasingReceive) && canAccessWarehouse(po.warehouseId) && RECEIVABLE_PO_STATUSES.includes(s);
  const outstanding = po.lines.reduce((sum, l) => sum + l.quantityOutstanding, 0);

  const actions: ReactNode[] = [];
  if (s === "Draft" && canCreate) {
    actions.push(
      <ButtonLink key="edit" href={`/purchase-orders/${po.id}/edit`} icon={<Pencil className="size-4" />}>
        Edit
      </ButtonLink>,
      <Button key="submit" icon={<Send className="size-4" />} onClick={() => setConfirm("submit")}>
        Submit
      </Button>,
    );
  }
  if (s === "PendingApproval" && hasPermission(P.purchasingApprove)) {
    actions.push(
      <Button key="reject" variant="secondary" icon={<X className="size-4" />} onClick={() => setConfirm("reject")}>
        Reject
      </Button>,
      <Button key="approve" icon={<Check className="size-4" />} onClick={() => setConfirm("approve")} disabled={youCreated} title={youCreated ? "You created this purchase order; another approver must approve it." : undefined}>
        Approve
      </Button>,
    );
  }
  if (canReceive && outstanding > 0)
    actions.push(
      <Button key="receive" icon={<Truck className="size-4" />} onClick={() => setReceiving(true)}>
        Receive goods
      </Button>,
    );
  if (s === "PartiallyReceived" && hasAnyPermission(P.purchasingCreate, P.purchasingApprove))
    actions.push(
      <Button key="close" variant="secondary" icon={<PackageCheck className="size-4" />} onClick={() => setConfirm("close")}>
        Close short
      </Button>,
    );
  if (hasPermission(P.payablesManage) && ["Approved", "PartiallyReceived", "Received", "Closed"].includes(s))
    actions.push(
      <Button key="bill" variant="secondary" icon={<FileText className="size-4" />} onClick={() => setBilling(true)}>
        Record bill
      </Button>,
    );
  if (canCreate && (s === "Draft" || s === "PendingApproval" || (s === "Approved" && po.receipts.length === 0)))
    actions.push(
      <Button key="cancel" variant="danger-ghost" icon={<Ban className="size-4" />} onClick={() => setConfirm("cancel")}>
        Cancel
      </Button>,
    );

  const c = confirm ? COPY[confirm] : null;
  const invoiced = po.invoices.filter((i) => i.status !== "Cancelled").reduce((sum, i) => sum + i.total, 0);

  return (
    <>
      <PageHeader
        breadcrumbs={[{ label: "Purchase orders", href: "/purchase-orders" }, { label: po.number }]}
        title={
          <span className="flex flex-wrap items-center gap-3">
            {po.number}
            <PurchaseOrderStatusBadge status={po.status} />
          </span>
        }
        description={
          <>
            {hasPermission(P.suppliersView) ? (
              <Link href={`/suppliers/${po.supplierId}`} className="font-medium text-slate-700 hover:text-brand-700 hover:underline">
                {po.supplierName}
              </Link>
            ) : (
              po.supplierName
            )}{" "}
            · ordered {formatDate(po.orderDate)}
            {po.expectedDate ? ` · expected ${formatDate(po.expectedDate)}` : ""} · receiving at {po.warehouseCode}
          </>
        }
        actions={actions.length ? actions : undefined}
      />

      {s === "PendingApproval" && (
        <div role="status" className="mb-6 flex gap-3 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
          <AlertTriangle className="mt-0.5 size-4 shrink-0 text-amber-600" aria-hidden />
          <div>
            <p className="font-medium">Waiting for approval</p>
            <p className="mt-0.5">The total of {formatCurrency(po.total, po.currency)} is above the organization&apos;s purchase approval threshold.</p>
            {hasPermission(P.purchasingApprove) && youCreated && <p className="mt-1 text-xs">You created this purchase order, so another approver must approve it.</p>}
          </div>
        </div>
      )}
      {s === "Rejected" && po.rejectionReason && (
        <div role="status" className="mb-6 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          <span className="font-medium">Rejected:</span> {po.rejectionReason}
        </div>
      )}
      {hasPermission(P.purchasingReceive) && RECEIVABLE_PO_STATUSES.includes(s) && !canAccessWarehouse(po.warehouseId) && (
        <div role="status" className="mb-6 rounded-lg border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-700">
          Goods for this order are received at {po.warehouseCode}, which you are not assigned to.
        </div>
      )}

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="min-w-0 space-y-6">
          <Card>
            <CardHeader title={`Lines (${po.lines.length})`} description={`${formatQty(outstanding)} unit(s) outstanding`} />
            <Table>
              <THead>
                <tr>
                  <TH>Product</TH>
                  <TH align="right">Ordered</TH>
                  <TH align="right">Received</TH>
                  <TH align="right">Outstanding</TH>
                  <TH align="right">Unit cost</TH>
                  <TH align="right">Tax</TH>
                  <TH align="right">Total</TH>
                </tr>
              </THead>
              <TBody>
                {po.lines.map((l) => (
                  <TR key={l.id}>
                    <TD className="max-w-64">
                      <Link href={`/products/${l.productId}`} className="block truncate font-medium text-slate-900 hover:text-brand-700 hover:underline">
                        {l.productName}
                      </Link>
                      <span className="font-mono text-xs text-slate-500">{l.sku}</span>
                    </TD>
                    <TD numeric className="font-medium text-slate-900">
                      {formatQty(l.quantity)}
                    </TD>
                    <TD numeric className={l.quantityReceived >= l.quantity ? "text-emerald-700" : undefined}>
                      {formatQty(l.quantityReceived)}
                    </TD>
                    <TD numeric className={l.quantityOutstanding > 0 ? "font-medium text-amber-700" : "text-slate-400"}>
                      {formatQty(l.quantityOutstanding)}
                    </TD>
                    <TD numeric>{formatCurrency(l.unitCost, po.currency, { precise: true })}</TD>
                    <TD numeric className="text-slate-500">
                      {formatPercent(l.taxRate)}
                    </TD>
                    <TD numeric className="font-medium text-slate-900">
                      {formatCurrency(l.total, po.currency)}
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
            <CardBody className="border-t border-slate-200">
              <dl className="ml-auto max-w-xs space-y-1.5 text-sm">
                <Amount label="Subtotal" value={formatCurrency(po.subtotal, po.currency)} />
                <Amount label="Tax" value={formatCurrency(po.taxTotal, po.currency)} />
                <Amount label="Total" value={formatCurrency(po.total, po.currency)} strong />
                <Amount label="Received value" value={formatCurrency(po.receivedValue, po.currency)} />
              </dl>
            </CardBody>
          </Card>

          <Card>
            <CardHeader title="Goods receipts" />
            {po.receipts.length === 0 ? (
              <EmptyState icon={<Truck />} title="Nothing received yet" description="Each delivery is booked as a goods received note (GRN) and stock in." className="py-8" />
            ) : (
              <Table>
                <THead>
                  <tr>
                    <TH>GRN</TH>
                    <TH>Received</TH>
                    <TH>By</TH>
                    <TH>Delivery note</TH>
                    <TH>Items</TH>
                    <TH>Notes</TH>
                  </tr>
                </THead>
                <TBody>
                  {[...po.receipts]
                    .sort((a, b) => b.receivedAt.localeCompare(a.receivedAt))
                    .map((r) => (
                      <TR key={r.id}>
                        <TD className="font-mono text-xs font-semibold text-slate-900">{r.number}</TD>
                        <TD className="whitespace-nowrap text-xs text-slate-500">{formatDateTime(r.receivedAt)}</TD>
                        <TD className="text-xs text-slate-600">{r.receivedByName ?? "—"}</TD>
                        <TD className="font-mono text-xs">{r.deliveryNote ?? "—"}</TD>
                        <TD className="text-xs">
                          {r.lines.map((l) => (
                            <div key={l.purchaseOrderLineId}>
                              <span className="font-mono">{l.sku}</span> × {formatQty(l.quantity)}
                            </div>
                          ))}
                        </TD>
                        <TD className="max-w-56 text-xs text-slate-600">{r.notes ?? "—"}</TD>
                      </TR>
                    ))}
                </TBody>
              </Table>
            )}
          </Card>

          {hasPermission(P.payablesView) && (
            <Card>
              <CardHeader title="Supplier invoices" description={po.invoices.length ? `${formatCurrency(invoiced, po.currency)} invoiced against ${formatCurrency(po.receivedValue, po.currency)} received` : undefined} />
              {po.invoices.length === 0 ? (
                <EmptyState icon={<FileText />} title="No bills yet" description="Bills linked to this order are checked against the goods received." className="py-8" />
              ) : (
                <Table>
                  <THead>
                    <tr>
                      <TH>Bill</TH>
                      <TH>Status</TH>
                      <TH>Match</TH>
                      <TH>Due</TH>
                      <TH align="right">Total</TH>
                      <TH align="right">Balance</TH>
                    </tr>
                  </THead>
                  <TBody>
                    {po.invoices.map((b) => (
                      <TR key={b.id} interactive onClick={() => router.push(`/payables/${b.id}`)}>
                        <TD>
                          <div className="font-mono text-xs font-semibold text-slate-900">{b.reference}</div>
                          <div className="font-mono text-xs text-slate-500">{b.supplierInvoiceNumber}</div>
                        </TD>
                        <TD>
                          <SupplierInvoiceStatusBadge status={b.status} />
                        </TD>
                        <TD>
                          <MatchBadge matches={b.matchesReceipts} hasPo />
                        </TD>
                        <TD className="text-xs text-slate-600">{formatDate(b.dueDate)}</TD>
                        <TD numeric>{formatCurrency(b.total, b.currency)}</TD>
                        <TD numeric className={b.balanceDue > 0 ? "font-semibold text-slate-900" : "text-slate-400"}>
                          {formatCurrency(b.balanceDue, b.currency)}
                        </TD>
                      </TR>
                    ))}
                  </TBody>
                </Table>
              )}
            </Card>
          )}
        </div>

        <div className="space-y-6">
          <Card>
            <CardHeader title="Status" />
            <CardBody>
              <Timeline po={po} />
            </CardBody>
          </Card>
          {po.notes && (
            <Card>
              <CardHeader title="Notes" />
              <CardBody className="whitespace-pre-wrap text-sm text-slate-700">{po.notes}</CardBody>
            </Card>
          )}
        </div>
      </div>

      <ConfirmDialog
        open={!!c}
        onClose={() => setConfirm(null)}
        title={c ? `${c.title} ${po.number}?` : ""}
        description={c?.body(po)}
        confirmLabel={c?.label}
        tone={c?.tone ?? "primary"}
        notes={c?.reason ? { label: "Reason for rejection", required: true, maxLength: 1000 } : undefined}
        onConfirm={async (text) => {
          if (!confirm || !c) return;
          const updated = await action.mutateAsync({ name: confirm, body: c.reason ? { reason: text } : undefined });
          if (confirm === "submit")
            toast.success(updated.status === "PendingApproval" ? `${po.number} submitted for approval` : `${po.number} approved`, updated.status === "PendingApproval" ? "The total is above the approval threshold." : "Within the approval threshold.");
          else toast.success(`${po.number} ${c.done}`);
        }}
      />
      <ReceiveGoodsDialog purchaseOrderId={po.id} open={receiving} onClose={() => setReceiving(false)} />
      <SupplierInvoiceDialog open={billing} onClose={() => setBilling(false)} presetSupplier={{ id: po.supplierId, code: "", name: po.supplierName }} presetPurchaseOrderId={po.id} />
    </>
  );
}
