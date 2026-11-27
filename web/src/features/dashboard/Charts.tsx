"use client";

import { BarChart3, Table2 } from "lucide-react";
import { useState } from "react";
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
  type TooltipContentProps,
} from "recharts";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { cn } from "@/lib/cn";
import { formatCompact, formatCurrency, formatDate, formatDayShort, formatQty } from "@/lib/format";
import type { TrendPoint } from "@/lib/types";

// Chart tokens (validated categorical slots 1–2 on the white surface; see dataviz palette).
// Orange is < 3:1 against white, so every chart offers a table view as relief.
const C = {
  inbound: "#257de0",
  outbound: "#f47230",
  value: "#257de0",
  grid: "#e7e7e7",
  axis: "#c2c2c2",
  ink: "#191919",
  inkSecondary: "#666666",
  surface: "#ffffff",
};

const tick = { fill: C.inkSecondary, fontSize: 11 };

function axisNumber(value: number): string {
  return Math.abs(value) >= 10_000 ? formatCompact(value) : new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(value);
}

/** Small segmented control to switch between a chart and its table view. */
export function ViewToggle({ view, onChange }: { view: "chart" | "table"; onChange: (v: "chart" | "table") => void }) {
  return (
    <div className="inline-flex rounded-md border border-slate-200 bg-white p-0.5" role="group" aria-label="View">
      {(["chart", "table"] as const).map((v) => (
        <button
          key={v}
          type="button"
          aria-pressed={view === v}
          onClick={() => onChange(v)}
          className={cn(
            "inline-flex items-center gap-1 rounded px-2 py-1 text-xs font-medium",
            view === v ? "bg-slate-100 text-slate-900" : "text-slate-500 hover:text-slate-800",
          )}
        >
          {v === "chart" ? <BarChart3 className="size-3.5" aria-hidden /> : <Table2 className="size-3.5" aria-hidden />}
          {v === "chart" ? "Chart" : "Table"}
        </button>
      ))}
    </div>
  );
}

export function useChartView() {
  return useState<"chart" | "table">("chart");
}

function LegendKey({ color, label }: { color: string; label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-xs text-slate-600">
      <span className="h-0.5 w-3 rounded-full" style={{ backgroundColor: color }} aria-hidden="true" />
      {label}
    </span>
  );
}

function TrendTooltip({ active, payload, label }: TooltipContentProps) {
  if (!active || !payload?.length) return null;
  const point = payload[0]?.payload as TrendPoint | undefined;
  if (!point) return null;
  return (
    <div className="rounded-md border border-slate-200 bg-white px-3 py-2 text-xs shadow-md">
      <p className="mb-1 font-medium text-slate-900">{formatDate(String(label ?? point.date))}</p>
      <p className="flex items-center justify-between gap-6 text-slate-600">
        <span className="inline-flex items-center gap-1.5">
          <span className="size-2 rounded-full" style={{ backgroundColor: C.inbound }} aria-hidden="true" /> Inbound
        </span>
        <span className="font-medium tabular-nums text-slate-900">{formatQty(point.inbound)}</span>
      </p>
      <p className="flex items-center justify-between gap-6 text-slate-600">
        <span className="inline-flex items-center gap-1.5">
          <span className="size-2 rounded-full" style={{ backgroundColor: C.outbound }} aria-hidden="true" /> Outbound
        </span>
        <span className="font-medium tabular-nums text-slate-900">{formatQty(point.outbound)}</span>
      </p>
    </div>
  );
}

/** Inbound vs outbound units per day (one shared y-axis, units). */
export function MovementTrendChart({ data, height = 280, view }: { data: TrendPoint[]; height?: number; view: "chart" | "table" }) {
  const totals = data.reduce((acc, p) => ({ inbound: acc.inbound + p.inbound, outbound: acc.outbound + p.outbound }), { inbound: 0, outbound: 0 });

  if (view === "table") {
    return (
      <Table maxHeight={`${height + 32}px`}>
        <THead>
          <tr>
            <TH>Date</TH>
            <TH align="right">Inbound</TH>
            <TH align="right">Outbound</TH>
            <TH align="right">Net</TH>
          </tr>
        </THead>
        <TBody>
          {[...data].reverse().map((p) => (
            <TR key={p.date}>
              <TD>{formatDate(p.date)}</TD>
              <TD numeric>{formatQty(p.inbound)}</TD>
              <TD numeric>{formatQty(p.outbound)}</TD>
              <TD numeric>{formatQty(p.inbound - p.outbound)}</TD>
            </TR>
          ))}
        </TBody>
      </Table>
    );
  }

  return (
    <div>
      <div className="mb-2 flex flex-wrap items-center gap-4 px-1">
        <LegendKey color={C.inbound} label={`Inbound · ${formatQty(totals.inbound)}`} />
        <LegendKey color={C.outbound} label={`Outbound · ${formatQty(totals.outbound)}`} />
      </div>
      <div style={{ height }} role="img" aria-label={`Movement trend over ${data.length} days: ${formatQty(totals.inbound)} units in, ${formatQty(totals.outbound)} units out.`}>
        <ResponsiveContainer width="100%" height="100%">
          <AreaChart data={data} margin={{ top: 8, right: 12, bottom: 0, left: 0 }}>
            <CartesianGrid vertical={false} stroke={C.grid} />
            <XAxis
              dataKey="date"
              tickFormatter={formatDayShort}
              tick={tick}
              tickLine={false}
              axisLine={{ stroke: C.axis }}
              minTickGap={24}
            />
            <YAxis tickFormatter={axisNumber} tick={tick} tickLine={false} axisLine={false} width={48} allowDecimals={false} />
            <Tooltip content={TrendTooltip} cursor={{ stroke: C.axis, strokeWidth: 1 }} />
            <Area
              type="monotone"
              dataKey="inbound"
              name="Inbound"
              stroke={C.inbound}
              strokeWidth={2}
              fill={C.inbound}
              fillOpacity={0.1}
              dot={false}
              activeDot={{ r: 4, stroke: C.surface, strokeWidth: 2, fill: C.inbound }}
              isAnimationActive={false}
            />
            <Area
              type="monotone"
              dataKey="outbound"
              name="Outbound"
              stroke={C.outbound}
              strokeWidth={2}
              fill={C.outbound}
              fillOpacity={0.1}
              dot={false}
              activeDot={{ r: 4, stroke: C.surface, strokeWidth: 2, fill: C.outbound }}
              isAnimationActive={false}
            />
          </AreaChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}

export interface ValueDatum {
  key: string;
  label: string;
  value: number;
  retail?: number;
  units?: number;
}

function ValueTooltip({ active, payload, currency }: TooltipContentProps & { currency: string }) {
  if (!active || !payload?.length) return null;
  const d = payload[0]?.payload as ValueDatum | undefined;
  if (!d) return null;
  return (
    <div className="rounded-md border border-slate-200 bg-white px-3 py-2 text-xs shadow-md">
      <p className="mb-1 font-medium text-slate-900">{d.label}</p>
      <p className="flex justify-between gap-6 text-slate-600">
        Cost value <span className="font-medium tabular-nums text-slate-900">{formatCurrency(d.value, currency)}</span>
      </p>
      {d.retail !== undefined && (
        <p className="flex justify-between gap-6 text-slate-600">
          Retail value <span className="font-medium tabular-nums text-slate-900">{formatCurrency(d.retail, currency)}</span>
        </p>
      )}
      {d.units !== undefined && (
        <p className="flex justify-between gap-6 text-slate-600">
          Units <span className="font-medium tabular-nums text-slate-900">{formatQty(d.units)}</span>
        </p>
      )}
    </div>
  );
}

/** Horizontal bars of stock value at cost (single series → no legend; the card title names it). */
export function ValueBarChart({ data, currency, view, emptyLabel = "No stock on hand" }: { data: ValueDatum[]; currency: string; view: "chart" | "table"; emptyLabel?: string }) {
  if (data.length === 0) {
    return <p className="py-12 text-center text-sm text-slate-500">{emptyLabel}</p>;
  }

  if (view === "table") {
    return (
      <Table>
        <THead>
          <tr>
            <TH>Name</TH>
            <TH align="right">Units</TH>
            <TH align="right">Cost value</TH>
            <TH align="right">Retail value</TH>
          </tr>
        </THead>
        <TBody>
          {data.map((d) => (
            <TR key={d.key}>
              <TD className="max-w-56 truncate">{d.label}</TD>
              <TD numeric>{d.units !== undefined ? formatQty(d.units) : "—"}</TD>
              <TD numeric>{formatCurrency(d.value, currency)}</TD>
              <TD numeric>{d.retail !== undefined ? formatCurrency(d.retail, currency) : "—"}</TD>
            </TR>
          ))}
        </TBody>
      </Table>
    );
  }

  const rowHeight = 36;
  const height = Math.max(120, data.length * rowHeight + 24);
  return (
    <div style={{ height }} role="img" aria-label={`Stock value at cost: ${data.map((d) => `${d.label} ${formatCurrency(d.value, currency)}`).join("; ")}`}>
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} layout="vertical" margin={{ top: 0, right: 64, bottom: 0, left: 0 }} barCategoryGap={2}>
          <CartesianGrid horizontal={false} stroke={C.grid} />
          <XAxis type="number" tickFormatter={(v: number) => formatCurrency(v, currency, { compact: true })} tick={tick} tickLine={false} axisLine={false} />
          <YAxis
            type="category"
            dataKey="label"
            width={150}
            tick={{ ...tick, fill: "#334155" }}
            tickLine={false}
            axisLine={{ stroke: C.axis }}
            tickFormatter={(v: string) => (v.length > 22 ? `${v.slice(0, 21)}…` : v)}
          />
          <Tooltip content={(props) => <ValueTooltip {...props} currency={currency} />} cursor={{ fill: "rgba(15, 23, 42, 0.04)" }} />
          <Bar
            dataKey="value"
            name="Cost value"
            fill={C.value}
            barSize={18}
            radius={[0, 4, 4, 0]}
            isAnimationActive={false}
            label={{
              position: "right",
              fill: C.inkSecondary,
              fontSize: 11,
              formatter: (v: unknown) => (typeof v === "number" ? formatCurrency(v, currency, { compact: true }) : ""),
            }}
          />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
