"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Ban, CheckCircle2, CircleAlert, CreditCard, Wallet } from "lucide-react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { EmptyState } from "@/components/ui/EmptyState";
import { ErrorState } from "@/components/ui/ErrorState";
import { PageHeader } from "@/components/ui/PageHeader";
import { Skeleton } from "@/components/ui/Skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { useToast } from "@/components/ui/Toast";
import { RequirePermission } from "@/features/common/RequirePermission";
import { SupplierInvoiceStatusBadge } from "@/features/procurement/ProcurementBadges";
import { SupplierPaymentDialog } from "@/features/procurement/SupplierPaymentDialog";
import { PAYMENT_METHOD_LABELS } from "@/features/sales/SalesBadges";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { formatCurrency, formatDate, formatDateTime } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import type { SupplierInvoice } from "@/lib/types";

export default function SupplierInvoicePage() {
  return (
    <RequirePermission anyOf={[P.payablesView]}>
      <BillView />
    </RequirePermission>
  );
}

function Row({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className={cn("flex justify-between gap-6", strong && "border-t border-slate-200 pt-2 text-base font-semibold")}>
      <dt className={strong ? "text-slate-900" : "text-slate-500"}>{label}</dt>
      <dd className="tabular-nums text-slate-900">{value}</dd>
    </div>
  );
}

function BillView() {
  const { id } = useParams<{ id: string }>();
  const { hasPermission } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [paying, setPaying] = useState(false);
  const [cancelling, setCancelling] = useState(false);

  const query = useQuery({ queryKey: qk.supplierInvoice(id), queryFn: () => api.get<SupplierInvoice>(`/api/supplier-invoices/${id}`) });
  const cancel = useMutation({
    mutationFn: () => api.post<SupplierInvoice>(`/api/supplier-invoices/${id}/cancel`),
    onSuccess: (bill) => {
      queryClient.setQueryData(qk.supplierInvoice(id), bill);
      for (const root of ["supplier-invoices", "suppliers", "purchase-orders"]) void queryClient.invalidateQueries({ queryKey: [root] });
      toast.success(`${bill.summary.reference} cancelled`);
    },
  });

  if (query.isPending) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-72" />
        <Skeleton className="h-48 w-full" />
      </div>
    );
  }
  if (query.isError || !query.data) {
    return (
      <Card>
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </Card>
    );
  }
  const bill = query.data;
  const s = bill.summary;
  const manage = hasPermission(P.payablesManage);
  const canPay = manage && s.balanceDue > 0 && s.status !== "Cancelled" && s.status !== "Paid";
  const canCancel = manage && s.status !== "Cancelled" && bill.payments.length === 0;

  return (
    <>
      <PageHeader
        breadcrumbs={[{ label: "Payables", href: "/payables" }, { label: s.reference }]}
        title={
          <span className="flex flex-wrap items-center gap-3">
            {s.reference}
            <SupplierInvoiceStatusBadge status={s.status} />
          </span>
        }
        description={
          <>
            {hasPermission(P.suppliersView) ? (
              <Link href={`/suppliers/${s.supplierId}`} className="font-medium text-slate-700 hover:text-brand-700 hover:underline">
                {s.supplierName}
              </Link>
            ) : (
              s.supplierName
            )}{" "}
            · invoice <span className="font-mono">{s.supplierInvoiceNumber}</span> dated {formatDate(s.invoiceDate)} · due {formatDate(s.dueDate)}
          </>
        }
        actions={
          <>
            {canCancel && (
              <Button variant="danger-ghost" icon={<Ban className="size-4" />} onClick={() => setCancelling(true)}>
                Cancel bill
              </Button>
            )}
            {canPay && (
              <Button icon={<CreditCard className="size-4" />} onClick={() => setPaying(true)}>
                Record payment
              </Button>
            )}
          </>
        }
      />

      {s.purchaseOrderId && (
        <div
          role="status"
          className={cn(
            "mb-6 flex gap-3 rounded-lg border px-4 py-3 text-sm",
            s.matchesReceipts ? "border-emerald-200 bg-emerald-50 text-emerald-900" : "border-amber-200 bg-amber-50 text-amber-900",
          )}
        >
          {s.matchesReceipts ? <CheckCircle2 className="mt-0.5 size-4 shrink-0" aria-hidden /> : <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />}
          <div>
            <p className="font-medium">{s.matchesReceipts ? "Three-way match passed" : "Three-way match failed — review before paying"}</p>
            <p className="mt-0.5">
              {s.matchesReceipts ? "Everything invoiced against " : "More has been invoiced than received on "}
              {hasPermission(P.purchasingView) ? (
                <Link href={`/purchase-orders/${s.purchaseOrderId}`} className="font-mono underline">
                  {s.purchaseOrderNumber}
                </Link>
              ) : (
                <span className="font-mono">{s.purchaseOrderNumber}</span>
              )}
              {s.matchesReceipts ? " is within the value of goods received." : "."}
            </p>
          </div>
        </div>
      )}

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <Card>
          <CardHeader
            title="Payments"
            actions={
              canPay ? (
                <Button size="sm" variant="secondary" icon={<CreditCard className="size-3.5" />} onClick={() => setPaying(true)}>
                  Record payment
                </Button>
              ) : null
            }
          />
          {bill.payments.length === 0 ? (
            <EmptyState icon={<Wallet />} title="No payments yet" description="Payments reduce the balance; the bill is marked paid once settled." className="py-8" />
          ) : (
            <Table>
              <THead>
                <tr>
                  <TH>Paid at</TH>
                  <TH>Method</TH>
                  <TH>Reference</TH>
                  <TH>Recorded by</TH>
                  <TH align="right">Amount</TH>
                </tr>
              </THead>
              <TBody>
                {bill.payments.map((p) => (
                  <TR key={p.id}>
                    <TD className="whitespace-nowrap text-xs text-slate-600">{formatDateTime(p.paidAt)}</TD>
                    <TD>{PAYMENT_METHOD_LABELS[p.method] ?? p.method}</TD>
                    <TD className="font-mono text-xs">{p.reference ?? "—"}</TD>
                    <TD className="text-xs text-slate-600">{p.recordedByName ?? "—"}</TD>
                    <TD numeric className="font-medium text-slate-900">
                      {formatCurrency(p.amount, s.currency)}
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
        </Card>
        <div className="space-y-6">
          <Card>
            <CardHeader title="Amounts" />
            <CardBody>
              <dl className="space-y-1.5 text-sm">
                <Row label="Subtotal" value={formatCurrency(bill.subtotal, s.currency)} />
                <Row label="Tax" value={formatCurrency(bill.taxTotal, s.currency)} />
                <Row label="Total" value={formatCurrency(s.total, s.currency)} strong />
                <Row label="Paid" value={s.amountPaid ? `−${formatCurrency(s.amountPaid, s.currency)}` : formatCurrency(0, s.currency)} />
                <Row label="Balance due" value={formatCurrency(s.balanceDue, s.currency)} strong />
              </dl>
            </CardBody>
          </Card>
          {bill.notes && (
            <Card>
              <CardHeader title="Notes" />
              <CardBody className="whitespace-pre-wrap text-sm text-slate-700">{bill.notes}</CardBody>
            </Card>
          )}
        </div>
      </div>

      <SupplierPaymentDialog bill={bill} open={paying} onClose={() => setPaying(false)} />
      <ConfirmDialog
        open={cancelling}
        onClose={() => setCancelling(false)}
        title={`Cancel ${s.reference}?`}
        description="The bill no longer counts towards what you owe this supplier or towards the purchase order's three-way match. Bills with payments cannot be cancelled."
        confirmLabel="Cancel bill"
        tone="danger"
        onConfirm={() => cancel.mutateAsync()}
      />
    </>
  );
}
