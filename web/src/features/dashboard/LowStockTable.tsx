import { CheckCircle2 } from "lucide-react";
import Link from "next/link";
import { Badge } from "@/components/ui/Badge";
import { EmptyState } from "@/components/ui/EmptyState";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { formatQty } from "@/lib/format";
import type { LowStockItem } from "@/lib/types";

export function LowStockTable({ items, showWarehouse = true }: { items: LowStockItem[]; showWarehouse?: boolean }) {
  if (items.length === 0) {
    return <EmptyState icon={<CheckCircle2 />} title="No low-stock items" description="Every active product is above its reorder level." />;
  }
  return (
    <Table>
      <THead>
        <tr>
          <TH>Product</TH>
          {showWarehouse && <TH>Warehouse</TH>}
          <TH align="right">Available</TH>
          <TH align="right">Reorder level</TH>
          <TH align="right">Suggested order</TH>
          <TH>
            <span className="sr-only">Status</span>
          </TH>
        </tr>
      </THead>
      <TBody>
        {items.map((item) => {
          const out = item.quantityAvailable <= 0;
          return (
            <TR key={`${item.warehouseId}-${item.productId}`}>
              <TD>
                <Link
                  href={`/inventory?warehouseId=${item.warehouseId}&search=${encodeURIComponent(item.sku)}`}
                  className="font-medium text-slate-900 hover:text-brand-700 hover:underline"
                >
                  {item.name}
                </Link>
                <div className="font-mono text-xs text-slate-500">{item.sku}</div>
              </TD>
              {showWarehouse && <TD className="font-mono text-xs">{item.warehouseCode}</TD>}
              <TD numeric className={out ? "font-semibold text-red-600" : "font-medium text-amber-700"}>
                {formatQty(item.quantityAvailable)}
              </TD>
              <TD numeric>{formatQty(item.reorderLevel)}</TD>
              <TD numeric>{item.reorderQuantity > 0 ? formatQty(item.reorderQuantity) : "—"}</TD>
              <TD>
                <Badge tone={out ? "danger" : "warning"} dot>
                  {out ? "Out of stock" : "Low stock"}
                </Badge>
              </TD>
            </TR>
          );
        })}
      </TBody>
    </Table>
  );
}

export function DaysSelector({ value, onChange, options = [7, 30, 90], unit = "days" }: { value: number; onChange: (d: number) => void; options?: number[]; unit?: string }) {
  return (
    <div className="inline-flex rounded-md border border-slate-200 bg-white p-0.5" role="group" aria-label="Time range">
      {options.map((d) => (
        <button
          key={d}
          type="button"
          aria-pressed={value === d}
          onClick={() => onChange(d)}
          className={
            value === d ? "rounded bg-brand-700 px-2.5 py-1 text-xs font-medium text-white" : "rounded px-2.5 py-1 text-xs font-medium text-slate-600 hover:bg-slate-100"
          }
        >
          {d} {unit}
        </button>
      ))}
    </div>
  );
}
