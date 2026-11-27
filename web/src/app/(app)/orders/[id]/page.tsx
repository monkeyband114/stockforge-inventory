"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle,
  Ban,
  Check,
  ClipboardList,
  Cog,
  Package,
  PackageCheck,
  Pencil,
  Receipt,
  ScanLine,
  Send,
  Truck,
  Undo2,
  X,
} from "lucide-react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState, type ReactNode } from "react";
import { Badge } from "@/components/ui/Badge";
import { Button, ButtonLink } from "@/components/ui/Button";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { EmptyState } from "@/components/ui/EmptyState";
import { ErrorState } from "@/components/ui/ErrorState";
import { PageHeader } from "@/components/ui/PageHeader";
import { Skeleton } from "@/components/ui/Skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { InvoicePdfButton } from "@/features/billing/InvoicePdfButton";
import { RequirePermission } from "@/features/common/RequirePermission";
import { formatAddress } from "@/features/sales/AddressFields";
import { PickDialog, ReturnDialog, ShipDialog } from "@/features/sales/FulfillmentDialogs";
import { OrderTimeline } from "@/features/sales/OrderTimeline";
import { InvoiceStatusBadge, OrderStatusBadge, ShipmentStatusBadge } from "@/features/sales/SalesBadges";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { formatCurrency, formatDate, formatDateTime, formatPercent, formatQty, fullName } from "@/lib/format";
import { useIdempotentSubmission } from "@/lib/hooks";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import type { Order, SalesOrderStatus } from "@/lib/types";

type SimpleAction = "submit" | "approve" | "reject" | "cancel" | "process" | "start-picking" | "pack";

const IDEMPOTENT = new Set<string>(["submit", "approve", "ship", "returns"]);
const CANCELLABLE: SalesOrderStatus[] = ["Draft", "PendingApproval", "Approved", "Processing", "Picking", "Packed"];
const RETURNABLE: SalesOrderStatus[] = ["Shipped", "Delivered", "PartiallyFulfilled", "Returned"];

const CONFIRM: Record<SimpleAction, { title: string; body: (o: Order) => string; label: string; tone?: "danger"; reason?: string; notes?: boolean; done: string }> = {
  submit: {
    title: "Submit order",
    body: () => "Orders within the approval thresholds are approved immediately: stock is reserved and the invoice is issued.",
    label: "Submit",
    done: "submitted",
  },
  approve: {
    title: "Approve order",
    body: (o) => `Reserves ${formatQty(o.totalQuantity)} unit(s) at ${o.warehouseCode} and issues the invoice in one step. If stock is short, nothing is changed.`,
    label: "Approve",
    notes: true,
    done: "approved",
  },
  reject: { title: "Reject order", body: () => "The customer's order is closed without reserving stock.", label: "Reject order", tone: "danger", reason: "Reason for rejection", done: "rejected" },
  cancel: { title: "Cancel order", body: () => "Any reserved stock is released. This cannot be undone.", label: "Cancel order", tone: "danger", reason: "Cancellation reason", done: "cancelled" },
  process: { title: "Start processing", body: (o) => `Hand ${o.number} to the warehouse team at ${o.warehouseCode}.`, label: "Start processing", done: "moved to processing" },
  "start-picking": { title: "Start picking", body: () => "Pickers can now record quantities taken from the shelves.", label: "Start picking", done: "moved to picking" },
  pack: { title: "Pack order", body: () => "Everything picked so far is packed and ready to ship.", label: "Pack", done: "packed" },
};

export default function OrderDetailPage() {
  return (
    <RequirePermission anyOf={[P.ordersView]}>
      <OrderDetailView />
    </RequirePermission>
  );
}

function Amount({ label, value, strong, negative }: { label: string; value: string; strong?: boolean; negative?: boolean }) {
  return (
    <div className={cn("flex justify-between gap-6", strong && "border-t border-slate-200 pt-2 text-base font-semibold")}>
      <dt className={strong ? "text-slate-900" : "text-slate-500"}>{label}</dt>
      <dd className="tabular-nums text-slate-900">
        {negative ? "−" : ""}
        {value}
      </dd>
    </div>
  );
}

function OrderDetailView() {
  const { id } = useParams<{ id: string }>();
  const { user, hasPermission, canAccessWarehouse } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const submission = useIdempotentSubmission();
  const [confirm, setConfirm] = useState<SimpleAction | null>(null);
  const [dialog, setDialog] = useState<"pick" | "ship" | "return" | null>(null);
  const [delivering, setDelivering] = useState<string | null>(null);

  const query = useQuery({ queryKey: qk.order(id), queryFn: () => api.get<Order>(`/api/orders/${id}`) });
  const order = query.data;

  const action = useMutation({
    mutationFn: async ({ path, body, key }: { path: string; body?: unknown; key?: string }) => {
      if (key) return (await api.postIdempotent<Order>(path, body ?? {}, key)).data;
      return api.post<Order>(path, body);
    },
    onSuccess: (updated) => {
      submission.reset();
      queryClient.setQueryData(qk.order(updated.id), updated);
      for (const root of ["orders", "fulfillment", "dashboard", "inventory", "reservations", "movements", "invoices", "customers", "finance"]) {
        void queryClient.invalidateQueries({ queryKey: [root], refetchType: root === "orders" ? "none" : "active" });
      }
    },
  });

  /** Runs an order action; idempotent ones get one key per (action, order version, payload). */
  const run = (name: string, body?: unknown, path = `/api/orders/${id}/${name}`) =>
    action.mutateAsync({ path, body, key: IDEMPOTENT.has(name) && order ? submission.keyFor({ id, name, v: order.version, body }) : undefined });

  if (query.isPending) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-72" />
        <Skeleton className="h-40 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }
  if (query.isError || !order) {
    return (
      <Card>
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </Card>
    );
  }

  const s = order.status;
  const operable = canAccessWarehouse(order.warehouseId);
  const fulfill = hasPermission(P.ordersFulfill) && operable;
  // createdByUserId is authoritative when present; the name comparison is only a fallback for older APIs.
  const youCreated = order.createdByUserId ? order.createdByUserId === user?.id : !!user && order.createdByName === fullName(user);
  const picked = order.lines.some((l) => l.quantityPicked > l.quantityPacked);
  const canReturn = hasPermission(P.ordersReturns) && operable && RETURNABLE.includes(s) && order.lines.some((l) => l.quantityShipped > l.quantityReturned);
  const hasShipments = order.shipments.length > 0;

  const actions: ReactNode[] = [];
  if (s === "Draft" && hasPermission(P.ordersCreate)) {
    actions.push(
      <ButtonLink key="edit" href={`/orders/${order.id}/edit`} icon={<Pencil className="size-4" />}>
        Edit
      </ButtonLink>,
      <Button key="submit" icon={<Send className="size-4" />} onClick={() => setConfirm("submit")}>
        Submit
      </Button>,
    );
  }
  if (s === "PendingApproval" && hasPermission(P.ordersApprove)) {
    actions.push(
      <Button key="reject" variant="secondary" icon={<X className="size-4" />} onClick={() => setConfirm("reject")}>
        Reject
      </Button>,
      <Button
        key="approve"
        icon={<Check className="size-4" />}
        onClick={() => setConfirm("approve")}
        disabled={youCreated}
        title={youCreated ? "You created this order; another approver must approve it." : undefined}
      >
        Approve
      </Button>,
    );
  }
  if (fulfill && s === "Approved")
    actions.push(
      <Button key="process" icon={<Cog className="size-4" />} onClick={() => setConfirm("process")}>
        Start processing
      </Button>,
    );
  if (fulfill && (s === "Processing" || s === "PartiallyFulfilled") && order.lines.some((l) => l.quantityPicked < l.quantity))
    actions.push(
      <Button key="start-picking" icon={<ClipboardList className="size-4" />} onClick={() => setConfirm("start-picking")}>
        Start picking
      </Button>,
    );
  if (fulfill && s === "Picking") {
    actions.push(
      <Button key="pick" variant={picked ? "secondary" : "primary"} icon={<ScanLine className="size-4" />} onClick={() => setDialog("pick")}>
        Record pick
      </Button>,
    );
    if (picked)
      actions.push(
        <Button key="pack" icon={<Package className="size-4" />} onClick={() => setConfirm("pack")}>
          Pack
        </Button>,
      );
  }
  if (fulfill && s === "Packed")
    actions.push(
      <Button key="ship" icon={<Truck className="size-4" />} onClick={() => setDialog("ship")}>
        Ship
      </Button>,
    );
  if (canReturn)
    actions.push(
      <Button key="return" variant="secondary" icon={<Undo2 className="size-4" />} onClick={() => setDialog("return")}>
        Create return
      </Button>,
    );
  if (CANCELLABLE.includes(s) && hasPermission(P.ordersCancel) && !hasShipments)
    actions.push(
      <Button key="cancel" variant="danger-ghost" icon={<Ban className="size-4" />} onClick={() => setConfirm("cancel")}>
        Cancel order
      </Button>,
    );

  const c = confirm ? CONFIRM[confirm] : null;
  const delivery = order.shipments.find((sh) => sh.id === delivering);

  return (
    <>
      <PageHeader
        breadcrumbs={[{ label: "Orders", href: "/orders" }, { label: order.number }]}
        title={
          <span className="flex flex-wrap items-center gap-3">
            {order.number}
            <OrderStatusBadge status={order.status} />
          </span>
        }
        description={
          <>
            {order.customerName} · ordered {formatDate(order.orderDate)}
            {order.requiredDate ? ` · required by ${formatDate(order.requiredDate)}` : ""} · ships from {order.warehouseCode}
          </>
        }
        actions={actions.length ? actions : undefined}
      />

      {s === "PendingApproval" && (
        <div role="status" className="mb-6 flex gap-3 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
          <AlertTriangle className="mt-0.5 size-4 shrink-0 text-amber-600" aria-hidden />
          <div>
            <p className="font-medium">Waiting for approval</p>
            <p className="mt-0.5">{order.approvalReason ?? "This order requires approval before stock is reserved."}</p>
            {hasPermission(P.ordersApprove) && youCreated && (
              <p className="mt-1 text-xs text-amber-800">You created this order, so another approver must approve it.</p>
            )}
          </div>
        </div>
      )}
      {s === "Rejected" && order.rejectionReason && (
        <div role="status" className="mb-6 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800">
          <span className="font-medium">Rejected:</span> {order.rejectionReason}
        </div>
      )}
      {s === "Cancelled" && order.cancellationReason && (
        <div role="status" className="mb-6 rounded-lg border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-700">
          <span className="font-medium">Cancelled:</span> {order.cancellationReason}
        </div>
      )}

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="min-w-0 space-y-6">
          <Card>
            <CardHeader title={`Lines (${order.lines.length})`} description={`${formatQty(order.totalQuantity)} units · prices in ${order.currency}`} />
            <Table>
              <THead>
                <tr>
                  <TH>Product</TH>
                  <TH align="right">Ordered</TH>
                  <TH align="right">Unit price</TH>
                  <TH align="right">Disc.</TH>
                  <TH align="right">Tax</TH>
                  <TH align="right">Total</TH>
                  <TH align="right">Picked</TH>
                  <TH align="right">Packed</TH>
                  <TH align="right">Shipped</TH>
                  <TH align="right">Returned</TH>
                  <TH align="right">Available</TH>
                </tr>
              </THead>
              <TBody>
                {order.lines.map((l) => {
                  const short = l.quantityAvailable !== null && ["Draft", "PendingApproval"].includes(s) && l.quantityAvailable < l.quantity;
                  return (
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
                      <TD numeric>{formatCurrency(l.unitPrice, order.currency)}</TD>
                      <TD numeric className="text-slate-500">
                        {l.discountPercent ? `${formatQty(l.discountPercent)}%` : "—"}
                      </TD>
                      <TD numeric className="text-slate-500">
                        {formatPercent(l.taxRate)}
                      </TD>
                      <TD numeric className="font-medium text-slate-900">
                        {formatCurrency(l.total, order.currency)}
                      </TD>
                      <TD numeric className={l.quantityPicked >= l.quantity ? "text-emerald-700" : undefined}>
                        {formatQty(l.quantityPicked)}
                      </TD>
                      <TD numeric>{formatQty(l.quantityPacked)}</TD>
                      <TD numeric className={l.quantityShipped >= l.quantity ? "text-emerald-700" : undefined}>
                        {formatQty(l.quantityShipped)}
                      </TD>
                      <TD numeric className={l.quantityReturned ? "text-amber-700" : "text-slate-400"}>
                        {formatQty(l.quantityReturned)}
                      </TD>
                      <TD numeric className={short ? "font-semibold text-red-600" : "text-slate-500"}>
                        {l.quantityAvailable !== null ? formatQty(l.quantityAvailable) : "—"}
                      </TD>
                    </TR>
                  );
                })}
              </TBody>
            </Table>
            <CardBody className="border-t border-slate-200">
              <dl className="ml-auto max-w-xs space-y-1.5 text-sm">
                <Amount label="Subtotal" value={formatCurrency(order.subtotal, order.currency)} />
                <Amount
                  label={`Discounts${order.discountPercent ? ` (incl. ${formatQty(order.discountPercent)}% order discount)` : ""}`}
                  value={formatCurrency(order.discountTotal, order.currency)}
                  negative={order.discountTotal > 0}
                />
                <Amount label="Tax" value={formatCurrency(order.taxTotal, order.currency)} />
                <Amount label="Total" value={formatCurrency(order.total, order.currency)} strong />
              </dl>
            </CardBody>
          </Card>

          <Card>
            <CardHeader title="Shipments" />
            {order.shipments.length === 0 ? (
              <EmptyState icon={<Truck />} title="Not shipped yet" description="Shipments appear here once packed items are shipped." className="py-8" />
            ) : (
              <Table>
                <THead>
                  <tr>
                    <TH>Shipment</TH>
                    <TH>Status</TH>
                    <TH>Carrier</TH>
                    <TH>Tracking</TH>
                    <TH align="right">Packages</TH>
                    <TH align="right">Units</TH>
                    <TH>Shipped</TH>
                    <TH>Delivered</TH>
                    {fulfill && (
                      <TH>
                        <span className="sr-only">Actions</span>
                      </TH>
                    )}
                  </tr>
                </THead>
                <TBody>
                  {order.shipments.map((sh) => (
                    <TR key={sh.id}>
                      <TD className="font-mono text-xs font-semibold text-slate-900">{sh.number}</TD>
                      <TD>
                        <ShipmentStatusBadge status={sh.status} />
                      </TD>
                      <TD>{sh.carrier ?? "—"}</TD>
                      <TD className="font-mono text-xs">{sh.trackingNumber ?? "—"}</TD>
                      <TD numeric>{sh.packageCount}</TD>
                      <TD numeric>{formatQty(sh.lines.reduce((sum, l) => sum + l.quantity, 0))}</TD>
                      <TD className="whitespace-nowrap text-xs text-slate-500">{formatDateTime(sh.shippedAt)}</TD>
                      <TD className="whitespace-nowrap text-xs text-slate-500">{sh.deliveredAt ? formatDateTime(sh.deliveredAt) : "—"}</TD>
                      {fulfill && (
                        <TD align="right">
                          {sh.status === "Shipped" && (
                            <Button size="sm" variant="secondary" icon={<PackageCheck className="size-3.5" />} onClick={() => setDelivering(sh.id)}>
                              Mark delivered
                            </Button>
                          )}
                        </TD>
                      )}
                    </TR>
                  ))}
                </TBody>
              </Table>
            )}
          </Card>

          {order.returns.length > 0 && (
            <Card>
              <CardHeader title="Returns" />
              <Table>
                <THead>
                  <tr>
                    <TH>Return</TH>
                    <TH>Received</TH>
                    <TH>Items</TH>
                    <TH>Reason</TH>
                    <TH align="right">Credit</TH>
                  </tr>
                </THead>
                <TBody>
                  {order.returns.map((r) => (
                    <TR key={r.id}>
                      <TD className="font-mono text-xs font-semibold text-slate-900">{r.number}</TD>
                      <TD className="whitespace-nowrap text-xs text-slate-500">{formatDateTime(r.receivedAt)}</TD>
                      <TD className="text-xs">
                        {r.lines.map((l) => (
                          <div key={l.salesOrderLineId}>
                            <span className="font-mono">{l.sku}</span> × {formatQty(l.quantity)}{" "}
                            <Badge tone={l.restock ? "success" : "neutral"}>{l.restock ? "Restocked" : "Not restocked"}</Badge>
                          </div>
                        ))}
                      </TD>
                      <TD className="max-w-64 text-xs text-slate-600">{r.reason}</TD>
                      <TD numeric className="font-medium text-slate-900">
                        {formatCurrency(r.creditAmount, order.currency)}
                      </TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            </Card>
          )}
        </div>

        <div className="space-y-6">
          <Card>
            <CardHeader title="Status" />
            <CardBody>
              <OrderTimeline order={order} />
            </CardBody>
          </Card>

          <Card>
            <CardHeader title="Invoice" />
            <CardBody>
              {order.invoice ? (
                <div className="space-y-3">
                  <div className="flex items-center justify-between gap-3">
                    {hasPermission(P.invoicesView) ? (
                      <Link href={`/invoices/${order.invoice.id}`} className="inline-flex items-center gap-2 font-mono text-sm font-semibold text-brand-700 hover:underline">
                        <Receipt className="size-4" aria-hidden />
                        {order.invoice.number}
                      </Link>
                    ) : (
                      <span className="font-mono text-sm font-semibold text-slate-900">{order.invoice.number}</span>
                    )}
                    <InvoiceStatusBadge status={order.invoice.status} />
                  </div>
                  <dl className="space-y-1.5 text-sm">
                    <Amount label="Invoice total" value={formatCurrency(order.invoice.total, order.currency)} />
                    <Amount label="Balance due" value={formatCurrency(order.invoice.balanceDue, order.currency)} strong />
                  </dl>
                  {hasPermission(P.invoicesView) && <InvoicePdfButton invoiceId={order.invoice.id} number={order.invoice.number} hasPdf={order.invoice.hasPdf} size="sm" />}
                </div>
              ) : (
                <p className="text-sm text-slate-500">The invoice is issued automatically when the order is approved.</p>
              )}
            </CardBody>
          </Card>

          <Card>
            <CardHeader title="Customer & delivery" />
            <CardBody className="space-y-3 text-sm">
              <div>
                <p className="font-medium text-slate-900">{order.customerName}</p>
                {order.customerEmail && <p className="text-slate-500">{order.customerEmail}</p>}
                {order.customerReference && (
                  <p className="mt-1 text-xs text-slate-500">
                    Customer ref. <span className="font-mono text-slate-700">{order.customerReference}</span>
                  </p>
                )}
              </div>
              <div>
                <p className="text-xs font-medium uppercase tracking-wide text-slate-500">Ship to</p>
                <p className="mt-0.5 text-slate-700">{formatAddress(order.shippingAddress) || "No shipping address"}</p>
              </div>
              <div>
                <p className="text-xs font-medium uppercase tracking-wide text-slate-500">Fulfilled from</p>
                <p className="mt-0.5 text-slate-700">
                  {order.warehouseName} ({order.warehouseCode})
                </p>
              </div>
              {order.notes && (
                <div>
                  <p className="text-xs font-medium uppercase tracking-wide text-slate-500">Notes</p>
                  <p className="mt-0.5 whitespace-pre-wrap text-slate-700">{order.notes}</p>
                </div>
              )}
            </CardBody>
          </Card>
        </div>
      </div>

      <ConfirmDialog
        open={!!c}
        onClose={() => setConfirm(null)}
        title={c ? `${c.title} ${order.number}?` : ""}
        description={c?.body(order)}
        confirmLabel={c?.label}
        tone={c?.tone ?? "primary"}
        notes={c?.reason ? { label: c.reason, required: true, maxLength: 1000 } : c?.notes ? { label: "Approval notes (optional)", maxLength: 1000 } : undefined}
        onConfirm={async (text) => {
          if (!confirm || !c) return;
          const body = c.reason ? { reason: text } : confirm === "approve" ? { notes: text || null } : undefined;
          const updated = await run(confirm, body);
          if (confirm === "submit") {
            toast.success(
              updated.status === "PendingApproval" ? `${order.number} submitted for approval` : `${order.number} approved`,
              updated.status === "PendingApproval" ? (updated.approvalReason ?? undefined) : "Stock reserved and invoice issued.",
            );
          } else toast.success(`Order ${order.number} ${c.done}`);
        }}
      />
      <PickDialog
        order={order}
        open={dialog === "pick"}
        onClose={() => setDialog(null)}
        onSubmit={async (body) => {
          await run("pick", body);
          toast.success("Pick recorded");
        }}
      />
      <ShipDialog
        order={order}
        open={dialog === "ship"}
        onClose={() => setDialog(null)}
        onSubmit={async (body) => {
          const updated = await run("ship", body);
          toast.success(updated.status === "PartiallyFulfilled" ? `${order.number} partially shipped` : `${order.number} shipped`);
        }}
      />
      <ReturnDialog
        order={order}
        open={dialog === "return"}
        onClose={() => setDialog(null)}
        onSubmit={async (body) => {
          await run("returns", body);
          toast.success("Return recorded", "Restocked items are back in stock and the invoice was credited.");
        }}
      />
      <ConfirmDialog
        open={!!delivery}
        onClose={() => setDelivering(null)}
        title={delivery ? `Mark ${delivery.number} delivered?` : ""}
        description="Confirms the customer received this shipment."
        confirmLabel="Mark delivered"
        onConfirm={async () => {
          if (!delivery) return;
          await run("deliver", undefined, `/api/shipments/${delivery.id}/deliver`);
          toast.success(`Shipment ${delivery.number} delivered`);
        }}
      />
    </>
  );
}
