import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { MovementTypeBadge, SignedQty } from "@/features/common/StatusBadges";
import { formatCurrency, formatDateTime, formatQty, humanize } from "@/lib/format";
import type { StockMovement } from "@/lib/types";

export function MovementsTable({
  movements,
  currency,
  showWarehouse = true,
  compact = false,
}: {
  movements: StockMovement[];
  currency: string;
  showWarehouse?: boolean;
  compact?: boolean;
}) {
  return (
    <Table>
      <THead>
        <tr>
          <TH>When</TH>
          <TH>Type</TH>
          <TH>Product</TH>
          {showWarehouse && <TH>Warehouse</TH>}
          <TH align="right">Qty change</TH>
          {!compact && <TH align="right">Reserved change</TH>}
          <TH align="right">On hand after</TH>
          {!compact && <TH align="right">Unit cost</TH>}
          <TH>Reference</TH>
          {!compact && <TH>Reason</TH>}
          <TH>By</TH>
        </tr>
      </THead>
      <TBody>
        {movements.map((m) => (
          <TR key={m.id}>
            <TD className="whitespace-nowrap text-xs text-slate-500">{formatDateTime(m.createdAt)}</TD>
            <TD>
              <MovementTypeBadge type={m.type} />
            </TD>
            <TD className="max-w-64">
              <div className="truncate font-medium text-slate-900">{m.productName}</div>
              <div className="font-mono text-xs text-slate-500">{m.sku}</div>
            </TD>
            {showWarehouse && <TD className="font-mono text-xs">{m.warehouseCode}</TD>}
            <TD numeric>{m.quantityDelta !== 0 ? <SignedQty value={m.quantityDelta} /> : <span className="text-slate-400">—</span>}</TD>
            {!compact && <TD numeric>{m.reservedDelta !== 0 ? <SignedQty value={m.reservedDelta} /> : <span className="text-slate-400">—</span>}</TD>}
            <TD numeric>{formatQty(m.onHandAfter)}</TD>
            {!compact && <TD numeric>{m.unitCost ? formatCurrency(m.unitCost, currency, { precise: true }) : "—"}</TD>}
            <TD className="text-xs">
              {m.referenceNumber ? <span className="font-mono text-slate-700">{m.referenceNumber}</span> : <span className="text-slate-400">—</span>}
              {m.referenceType !== "Manual" && <div className="text-slate-500">{humanize(m.referenceType)}</div>}
            </TD>
            {!compact && (
              <TD className="max-w-56 truncate text-xs text-slate-600" title={m.reason ?? undefined}>
                {m.reason ?? "—"}
              </TD>
            )}
            <TD className="whitespace-nowrap text-xs text-slate-600">{m.createdByName ?? "System"}</TD>
          </TR>
        ))}
      </TBody>
    </Table>
  );
}
