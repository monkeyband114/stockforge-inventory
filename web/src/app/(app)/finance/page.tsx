"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, FileText, HandCoins, Receipt, Wallet } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { Card, CardBody, CardHeader, StatCard } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { ErrorState } from "@/components/ui/ErrorState";
import { PageHeader } from "@/components/ui/PageHeader";
import { ChartSkeleton, StatGridSkeleton, TableSkeleton } from "@/components/ui/Skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { RequirePermission } from "@/features/common/RequirePermission";
import { ViewToggle, useChartView } from "@/features/dashboard/Charts";
import { DaysSelector } from "@/features/dashboard/LowStockTable";
import { InvoicedCollectedChart, monthLabel } from "@/features/dashboard/SalesCharts";
import { InvoiceStatusBadge } from "@/features/sales/SalesBadges";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatCurrency, formatDate, formatInteger, formatRelative } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import type { FinanceSummary } from "@/lib/types";

export default function FinancePage() {
  return (
    <RequirePermission anyOf={[P.financeReports]}>
      <FinanceView />
    </RequirePermission>
  );
}

function daysOverdue(due: string): number {
  const [y, m, d] = due.split("-").map(Number);
  return Math.max(0, Math.floor((Date.now() - new Date(y, m - 1, d).getTime()) / 86_400_000));
}

function FinanceView() {
  const { hasPermission } = useAuth();
  const router = useRouter();
  const [months, setMonths] = useState(12);
  const [view, setView] = useChartView();

  const query = useQuery({
    queryKey: qk.financeSummary(months),
    queryFn: () => api.get<FinanceSummary>("/api/finance/summary", { months }),
    placeholderData: keepPreviousData,
    refetchInterval: 60_000,
  });
  const d = query.data;
  const cur = d?.currency ?? "USD";
  const now = new Date();

  return (
    <>
      <PageHeader
        title="Finance overview"
        description="Receivables, collections and overdue invoices."
        meta={query.dataUpdatedAt ? <span className="text-xs text-slate-400">Updated {formatRelative(new Date(query.dataUpdatedAt).toISOString())}</span> : null}
      />
      {query.isError && !d ? (
        <Card>
          <ErrorState error={query.error} onRetry={() => void query.refetch()} />
        </Card>
      ) : (
        <div className="space-y-6">
          {!d ? (
            <StatGridSkeleton count={5} />
          ) : (
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-5">
              <StatCard label={`Invoiced · ${monthLabel(now.getFullYear(), now.getMonth() + 1, true)}`} value={formatCurrency(d.invoicedThisMonth, cur)} icon={<FileText />} tone="info" />
              <StatCard label="Collected this month" value={formatCurrency(d.collectedThisMonth, cur)} icon={<HandCoins />} tone="success" />
              <StatCard
                label="Outstanding"
                value={formatCurrency(d.outstandingTotal, cur)}
                sub={`${formatInteger(d.outstandingInvoices)} open invoice${d.outstandingInvoices === 1 ? "" : "s"}`}
                icon={<Receipt />}
                href={hasPermission(P.invoicesView) ? "/invoices?outstanding=true" : undefined}
              />
              <StatCard
                label="Overdue"
                value={formatCurrency(d.overdueTotal, cur)}
                sub={`${formatInteger(d.overdueInvoices)} overdue invoice${d.overdueInvoices === 1 ? "" : "s"}`}
                icon={<AlertTriangle />}
                tone={d.overdueInvoices > 0 ? "danger" : "success"}
                href={hasPermission(P.invoicesView) ? "/invoices?status=Overdue" : undefined}
              />
              <StatCard label="Payments · last 30 days" value={formatCurrency(d.paymentsLast30Days, cur)} icon={<Wallet />} />
            </div>
          )}

          <Card>
            <CardHeader
              title="Invoiced vs collected"
              description={`Per month, in ${cur}`}
              actions={
                <>
                  <DaysSelector value={months} onChange={setMonths} options={[6, 12, 24]} unit="months" />
                  <ViewToggle view={view} onChange={setView} />
                </>
              }
            />
            <CardBody className={query.isFetching && d ? "opacity-70 transition-opacity" : undefined}>
              {d ? <InvoicedCollectedChart data={d.monthly} currency={cur} view={view} /> : <ChartSkeleton height={280} />}
            </CardBody>
          </Card>

          <Card>
            <CardHeader
              title="Top overdue invoices"
              actions={
                hasPermission(P.invoicesView) ? (
                  <Link href="/invoices?status=Overdue" className="text-xs font-medium text-brand-700 hover:text-brand-600">
                    All overdue →
                  </Link>
                ) : null
              }
            />
            {!d ? (
              <TableSkeleton rows={4} columns={6} />
            ) : d.topOverdue.length === 0 ? (
              <EmptyState icon={<CheckCircle2 />} title="Nothing overdue" description="Every issued invoice is within its payment terms." />
            ) : (
              <Table>
                <THead>
                  <tr>
                    <TH>Invoice</TH>
                    <TH>Customer</TH>
                    <TH>Status</TH>
                    <TH>Due</TH>
                    <TH align="right">Days overdue</TH>
                    <TH align="right">Total</TH>
                    <TH align="right">Balance</TH>
                  </tr>
                </THead>
                <TBody>
                  {d.topOverdue.map((i) => (
                    <TR key={i.id} interactive={hasPermission(P.invoicesView)} onClick={hasPermission(P.invoicesView) ? () => router.push(`/invoices/${i.id}`) : undefined}>
                      <TD className="font-mono text-xs font-semibold text-slate-900">{i.number}</TD>
                      <TD className="font-medium text-slate-800">{i.customerName}</TD>
                      <TD>
                        <InvoiceStatusBadge status={i.status} />
                      </TD>
                      <TD className="whitespace-nowrap text-xs text-slate-600">{formatDate(i.dueDate)}</TD>
                      <TD numeric className="font-semibold text-red-600">
                        {formatInteger(daysOverdue(i.dueDate))}
                      </TD>
                      <TD numeric>{formatCurrency(i.total, i.currency)}</TD>
                      <TD numeric className="font-semibold text-slate-900">
                        {formatCurrency(i.balanceDue, i.currency)}
                      </TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            )}
          </Card>
        </div>
      )}
    </>
  );
}
