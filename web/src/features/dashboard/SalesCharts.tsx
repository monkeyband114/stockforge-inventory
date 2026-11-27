"use client";

import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis, type TooltipContentProps } from "recharts";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { ORDER_STATUS_LABELS } from "@/features/sales/SalesBadges";
import { formatCurrency, formatInteger } from "@/lib/format";
import type { MonthlyFinancePoint, MonthlySalesPoint, SalesOrderStatus, StatusCount } from "@/lib/types";

// Same validated categorical slots as Charts.tsx (blue #257de0, orange #f47230); orange needs the table view.
const C = { a: "#257de0", b: "#f47230", grid: "#e7e7e7", axis: "#c2c2c2", surface: "#ffffff", ink2: "#666666" };
const tick = { fill: C.ink2, fontSize: 11 };

export function monthLabel(year: number, month: number, long = false): string {
  return new Intl.DateTimeFormat(undefined, long ? { month: "long", year: "numeric" } : { month: "short", year: "2-digit" }).format(new Date(year, month - 1, 1));
}

function Key({ color, label }: { color: string; label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-xs text-slate-600">
      <span className="size-2.5 rounded-sm" style={{ backgroundColor: color }} aria-hidden="true" />
      {label}
    </span>
  );
}

// ---------- Invoiced vs collected (finance) ----------

interface FinanceRow extends MonthlyFinancePoint {
  label: string;
}

function FinanceTooltip({ active, payload, currency }: TooltipContentProps & { currency: string }) {
  if (!active || !payload?.length) return null;
  const d = payload[0]?.payload as FinanceRow | undefined;
  if (!d) return null;
  return (
    <div className="rounded-md border border-slate-200 bg-white px-3 py-2 text-xs shadow-md">
      <p className="mb-1 font-medium text-slate-900">{monthLabel(d.year, d.month, true)}</p>
      <p className="flex justify-between gap-6 text-slate-600">
        <span className="inline-flex items-center gap-1.5">
          <span className="size-2 rounded-sm" style={{ backgroundColor: C.a }} aria-hidden="true" /> Invoiced
        </span>
        <span className="font-medium tabular-nums text-slate-900">{formatCurrency(d.invoiced, currency)}</span>
      </p>
      <p className="flex justify-between gap-6 text-slate-600">
        <span className="inline-flex items-center gap-1.5">
          <span className="size-2 rounded-sm" style={{ backgroundColor: C.b }} aria-hidden="true" /> Collected
        </span>
        <span className="font-medium tabular-nums text-slate-900">{formatCurrency(d.collected, currency)}</span>
      </p>
    </div>
  );
}

export function InvoicedCollectedChart({ data, currency, view, height = 280 }: { data: MonthlyFinancePoint[]; currency: string; view: "chart" | "table"; height?: number }) {
  const rows: FinanceRow[] = data.map((d) => ({ ...d, label: monthLabel(d.year, d.month) }));
  if (view === "table") {
    return (
      <Table maxHeight={`${height + 32}px`}>
        <THead>
          <tr>
            <TH>Month</TH>
            <TH align="right">Invoiced</TH>
            <TH align="right">Collected</TH>
            <TH align="right">Difference</TH>
          </tr>
        </THead>
        <TBody>
          {[...rows].reverse().map((r) => (
            <TR key={`${r.year}-${r.month}`}>
              <TD>{monthLabel(r.year, r.month, true)}</TD>
              <TD numeric>{formatCurrency(r.invoiced, currency)}</TD>
              <TD numeric>{formatCurrency(r.collected, currency)}</TD>
              <TD numeric>{formatCurrency(r.invoiced - r.collected, currency)}</TD>
            </TR>
          ))}
        </TBody>
      </Table>
    );
  }
  const totals = rows.reduce((a, r) => ({ invoiced: a.invoiced + r.invoiced, collected: a.collected + r.collected }), { invoiced: 0, collected: 0 });
  return (
    <div>
      <div className="mb-2 flex flex-wrap gap-4 px-1">
        <Key color={C.a} label={`Invoiced · ${formatCurrency(totals.invoiced, currency, { compact: true })}`} />
        <Key color={C.b} label={`Collected · ${formatCurrency(totals.collected, currency, { compact: true })}`} />
      </div>
      <div style={{ height }} role="img" aria-label={`Invoiced vs collected over ${rows.length} months: ${formatCurrency(totals.invoiced, currency)} invoiced, ${formatCurrency(totals.collected, currency)} collected.`}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={rows} margin={{ top: 8, right: 8, bottom: 0, left: 0 }} barGap={2} barCategoryGap="28%">
            <CartesianGrid vertical={false} stroke={C.grid} />
            <XAxis dataKey="label" tick={tick} tickLine={false} axisLine={{ stroke: C.axis }} />
            <YAxis tickFormatter={(v: number) => formatCurrency(v, currency, { compact: true })} tick={tick} tickLine={false} axisLine={false} width={64} />
            <Tooltip content={(p) => <FinanceTooltip {...p} currency={currency} />} cursor={{ fill: "rgba(15, 23, 42, 0.04)" }} />
            <Bar dataKey="invoiced" name="Invoiced" fill={C.a} maxBarSize={20} radius={[4, 4, 0, 0]} isAnimationActive={false} />
            <Bar dataKey="collected" name="Collected" fill={C.b} maxBarSize={20} radius={[4, 4, 0, 0]} isAnimationActive={false} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}

// ---------- Monthly sales (single series, no legend) ----------

interface SalesRow extends MonthlySalesPoint {
  label: string;
}

function SalesTooltip({ active, payload, currency }: TooltipContentProps & { currency: string }) {
  if (!active || !payload?.length) return null;
  const d = payload[0]?.payload as SalesRow | undefined;
  if (!d) return null;
  return (
    <div className="rounded-md border border-slate-200 bg-white px-3 py-2 text-xs shadow-md">
      <p className="mb-1 font-medium text-slate-900">{monthLabel(d.year, d.month, true)}</p>
      <p className="flex justify-between gap-6 text-slate-600">
        Invoiced sales <span className="font-medium tabular-nums text-slate-900">{formatCurrency(d.sales, currency)}</span>
      </p>
      <p className="flex justify-between gap-6 text-slate-600">
        Invoices <span className="font-medium tabular-nums text-slate-900">{formatInteger(d.orders)}</span>
      </p>
    </div>
  );
}

export function MonthlySalesChart({ data, currency, view, height = 240 }: { data: MonthlySalesPoint[]; currency: string; view: "chart" | "table"; height?: number }) {
  const rows: SalesRow[] = data.map((d) => ({ ...d, label: monthLabel(d.year, d.month) }));
  if (view === "table") {
    return (
      <Table>
        <THead>
          <tr>
            <TH>Month</TH>
            <TH align="right">Sales</TH>
            <TH align="right">Invoices</TH>
          </tr>
        </THead>
        <TBody>
          {[...rows].reverse().map((r) => (
            <TR key={`${r.year}-${r.month}`}>
              <TD>{monthLabel(r.year, r.month, true)}</TD>
              <TD numeric>{formatCurrency(r.sales, currency)}</TD>
              <TD numeric>{formatInteger(r.orders)}</TD>
            </TR>
          ))}
        </TBody>
      </Table>
    );
  }
  return (
    <div style={{ height }} role="img" aria-label={`Monthly sales: ${rows.map((r) => `${r.label} ${formatCurrency(r.sales, currency)}`).join("; ")}`}>
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={rows} margin={{ top: 16, right: 8, bottom: 0, left: 0 }} barCategoryGap="30%">
          <CartesianGrid vertical={false} stroke={C.grid} />
          <XAxis dataKey="label" tick={tick} tickLine={false} axisLine={{ stroke: C.axis }} />
          <YAxis tickFormatter={(v: number) => formatCurrency(v, currency, { compact: true })} tick={tick} tickLine={false} axisLine={false} width={64} />
          <Tooltip content={(p) => <SalesTooltip {...p} currency={currency} />} cursor={{ fill: "rgba(15, 23, 42, 0.04)" }} />
          <Bar dataKey="sales" name="Sales" fill={C.a} maxBarSize={24} radius={[4, 4, 0, 0]} isAnimationActive={false} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

// ---------- Order status breakdown (meters with labels; never colour alone) ----------

export function OrderStatusBreakdown({ data, onSelect }: { data: StatusCount[]; onSelect?: (status: string) => void }) {
  const total = data.reduce((s, d) => s + d.count, 0);
  const max = Math.max(1, ...data.map((d) => d.count));
  if (total === 0) return <p className="py-8 text-center text-sm text-slate-500">No orders yet.</p>;
  return (
    <ul className="space-y-2">
      {[...data]
        .sort((a, b) => b.count - a.count)
        .map((d) => {
          const label = ORDER_STATUS_LABELS[d.status as SalesOrderStatus] ?? d.status;
          const content = (
            <>
              <span className="w-36 shrink-0 truncate text-sm text-slate-700">{label}</span>
              <span className="h-2 flex-1 overflow-hidden rounded-full bg-blue-50" aria-hidden="true">
                <span className="block h-full rounded-full" style={{ width: `${(d.count / max) * 100}%`, backgroundColor: C.a }} />
              </span>
              <span className="w-16 shrink-0 text-right text-sm tabular-nums text-slate-900">
                {formatInteger(d.count)} <span className="text-xs text-slate-400">{Math.round((d.count / total) * 100)}%</span>
              </span>
            </>
          );
          return (
            <li key={d.status}>
              {onSelect ? (
                <button type="button" onClick={() => onSelect(d.status)} className="flex w-full items-center gap-3 rounded px-1 py-0.5 text-left hover:bg-slate-50" aria-label={`${label}: ${d.count} orders`}>
                  {content}
                </button>
              ) : (
                <div className="flex items-center gap-3 px-1 py-0.5">{content}</div>
              )}
            </li>
          );
        })}
    </ul>
  );
}
