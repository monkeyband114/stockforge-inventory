"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CreditCard, RefreshCw, Wallet } from "lucide-react";
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
import { InvoicePdfButton } from "@/features/billing/InvoicePdfButton";
import { RecordPaymentDialog } from "@/features/billing/RecordPaymentDialog";
import { RequirePermission } from "@/features/common/RequirePermission";
import { formatAddress } from "@/features/sales/AddressFields";
import { InvoiceStatusBadge, PAYMENT_METHOD_LABELS } from "@/features/sales/SalesBadges";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { formatCurrency, formatDate, formatDateTime, formatPercent, formatQty } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import type { Invoice } from "@/lib/types";

export default function InvoiceDetailPage() {
  return (
    <RequirePermission anyOf={[P.invoicesView]}>
      <InvoiceDetailView />
    </RequirePermission>
  );
}

function Row({ label, value, strong, muted }: { label: string; value: string; strong?: boolean; muted?: boolean }) {
  return (
    <div className={cn("flex justify-between gap-6", strong && "border-t border-slate-200 pt-2 text-base font-semibold")}>
      <dt className={strong ? "text-slate-900" : "text-slate-500"}>{label}</dt>
      <dd className={cn("tabular-nums", muted ? "text-slate-500" : "text-slate-900")}>{value}</dd>
    </div>
  );
}

function InvoiceDetailView() {
  const { id } = useParams<{ id: string }>();
  const { hasPermission } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [paying, setPaying] = useState(false);
  const [regenerating, setRegenerating] = useState(false);

  const query = useQuery({ queryKey: qk.invoice(id), queryFn: () => api.get<Invoice>(`/api/invoices/${id}`) });
  const invoice = query.data;

  const regenerate = useMutation({
    mutationFn: () => api.post(`/api/invoices/${id}/regenerate-pdf`),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["invoices"] });
      toast.info("PDF regeneration queued", "The background worker renders the document; download it once it is ready.");
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
  if (query.isError || !invoice) {
    return (
      <Card>
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </Card>
    );
  }

  const cur = invoice.currency;
  const canPay = hasPermission(P.paymentsRecord) && invoice.balanceDue > 0 && !["Paid", "Cancelled", "Draft"].includes(invoice.status);

  return (
    <>
      <PageHeader
        breadcrumbs={[{ label: "Invoices", href: "/invoices" }, { label: invoice.number }]}
        title={
          <span className="flex flex-wrap items-center gap-3">
            {invoice.number}
            <InvoiceStatusBadge status={invoice.status} />
          </span>
        }
        description={
          <>
            {invoice.billingName} · issued {formatDate(invoice.issueDate)} · due {formatDate(invoice.dueDate)}
            {invoice.orderNumber && invoice.salesOrderId && (
              <>
                {" · order "}
                {hasPermission(P.ordersView) ? (
                  <Link href={`/orders/${invoice.salesOrderId}`} className="font-mono text-brand-700 hover:underline">
                    {invoice.orderNumber}
                  </Link>
                ) : (
                  <span className="font-mono">{invoice.orderNumber}</span>
                )}
              </>
            )}
          </>
        }
        actions={
          <>
            <InvoicePdfButton key={`${invoice.hasPdf}`} invoiceId={invoice.id} number={invoice.number} hasPdf={invoice.hasPdf} />
            {hasPermission(P.invoicesManage) && (
              <Button variant="secondary" icon={<RefreshCw className="size-4" />} onClick={() => setRegenerating(true)}>
                Regenerate PDF
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

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="min-w-0 space-y-6">
          <Card>
            <CardHeader title="Lines" />
            <Table>
              <THead>
                <tr>
                  <TH>#</TH>
                  <TH>Description</TH>
                  <TH align="right">Qty</TH>
                  <TH align="right">Unit price</TH>
                  <TH align="right">Discount</TH>
                  <TH align="right">Tax</TH>
                  <TH align="right">Total</TH>
                </tr>
              </THead>
              <TBody>
                {invoice.lines.map((l) => (
                  <TR key={l.lineNumber}>
                    <TD className="text-slate-500">{l.lineNumber}</TD>
                    <TD>
                      <div className="font-medium text-slate-900">{l.description}</div>
                      <div className="font-mono text-xs text-slate-500">{l.sku}</div>
                    </TD>
                    <TD numeric>{formatQty(l.quantity)}</TD>
                    <TD numeric>{formatCurrency(l.unitPrice, cur)}</TD>
                    <TD numeric className="text-slate-500">
                      {l.discountAmount ? `−${formatCurrency(l.discountAmount, cur)}` : "—"}
                    </TD>
                    <TD numeric className="text-slate-500">
                      {formatCurrency(l.taxAmount, cur)} <span className="text-xs">({formatPercent(l.taxRate)})</span>
                    </TD>
                    <TD numeric className="font-medium text-slate-900">
                      {formatCurrency(l.total, cur)}
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          </Card>

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
            {invoice.payments.length === 0 ? (
              <EmptyState icon={<Wallet />} title="No payments yet" description="Recorded payments reduce the balance due." className="py-8" />
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
                  {invoice.payments.map((p) => (
                    <TR key={p.id}>
                      <TD className="whitespace-nowrap text-xs text-slate-600">{formatDateTime(p.paidAt)}</TD>
                      <TD>{PAYMENT_METHOD_LABELS[p.method] ?? p.method}</TD>
                      <TD className="font-mono text-xs">{p.reference ?? "—"}</TD>
                      <TD className="text-xs text-slate-600">{p.recordedByName ?? "—"}</TD>
                      <TD numeric className="font-medium text-emerald-700">
                        {formatCurrency(p.amount, cur)}
                      </TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            )}
          </Card>
        </div>

        <div className="space-y-6">
          <Card>
            <CardHeader title="Amounts" />
            <CardBody>
              <dl className="space-y-1.5 text-sm">
                <Row label="Subtotal" value={formatCurrency(invoice.subtotal, cur)} />
                <Row label="Discounts" value={invoice.discountTotal ? `−${formatCurrency(invoice.discountTotal, cur)}` : formatCurrency(0, cur)} />
                <Row label="Tax" value={formatCurrency(invoice.taxTotal, cur)} />
                <Row label="Invoice total" value={formatCurrency(invoice.total, cur)} strong />
                <Row label="Paid" value={invoice.amountPaid ? `−${formatCurrency(invoice.amountPaid, cur)}` : formatCurrency(0, cur)} muted />
                <Row label="Credits (returns)" value={invoice.creditedAmount ? `−${formatCurrency(invoice.creditedAmount, cur)}` : formatCurrency(0, cur)} muted />
                <Row label="Balance due" value={formatCurrency(invoice.balanceDue, cur)} strong />
              </dl>
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Bill to" />
            <CardBody className="space-y-1 text-sm">
              <p className="font-medium text-slate-900">{invoice.billingName}</p>
              {invoice.billingEmail && <p className="text-slate-500">{invoice.billingEmail}</p>}
              {invoice.billingTaxNumber && <p className="text-xs text-slate-500">Tax no. {invoice.billingTaxNumber}</p>}
              <p className="text-slate-700">{formatAddress(invoice.billingAddress) || "No billing address"}</p>
            </CardBody>
          </Card>
          <Card>
            <CardHeader title="Document" />
            <CardBody className="text-sm text-slate-600">
              {invoice.hasPdf ? (
                <p>PDF generated {invoice.pdfGeneratedAt ? formatDateTime(invoice.pdfGeneratedAt) : ""}.</p>
              ) : (
                <p>The PDF is rendered by the background worker and will be available shortly after issue.</p>
              )}
            </CardBody>
          </Card>
        </div>
      </div>

      <RecordPaymentDialog invoice={invoice} open={paying} onClose={() => setPaying(false)} />
      <ConfirmDialog
        open={regenerating}
        onClose={() => setRegenerating(false)}
        title={`Regenerate PDF for ${invoice.number}?`}
        description="The document is re-rendered in the background with the current invoice data."
        confirmLabel="Regenerate"
        onConfirm={() => regenerate.mutateAsync()}
      />
    </>
  );
}
