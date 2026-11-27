"use client";

import { useQuery } from "@tanstack/react-query";
import { CheckCircle2, ClipboardList, Lightbulb } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { FilterSelect, QueryBoundary } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { setPoPrefill } from "@/features/procurement/poPrefill";
import { api } from "@/lib/api";
import { formatCurrency, formatQty } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useCurrency, useWarehouses } from "@/lib/queries";
import type { ReorderSuggestion } from "@/lib/types";

const rowKey = (s: ReorderSuggestion) => `${s.warehouseId}:${s.productId}`;

interface Group {
  key: string;
  supplierId: string | null;
  supplierName: string;
  warehouseId: string;
  warehouseCode: string;
  rows: ReorderSuggestion[];
  value: number;
}

export default function SuggestionsPage() {
  return (
    <RequirePermission anyOf={[P.purchasingCreate]}>
      <SuggestionsView />
    </RequirePermission>
  );
}

function SuggestionsView() {
  const router = useRouter();
  const currency = useCurrency();
  const warehouses = useWarehouses();
  const [warehouseId, setWarehouseId] = useState("");
  const [selected, setSelected] = useState<Set<string>>(new Set());

  const query = useQuery({
    queryKey: qk.reorderSuggestions(warehouseId),
    queryFn: () => api.get<ReorderSuggestion[]>("/api/purchase-orders/suggestions", { warehouseId: warehouseId || undefined }),
  });
  const rows = useMemo(() => query.data ?? [], [query.data]);

  // A purchase order has one supplier and one receiving warehouse, so selections group by both.
  const groups = useMemo(() => {
    const map = new Map<string, Group>();
    for (const r of rows) {
      if (!selected.has(rowKey(r))) continue;
      const key = `${r.preferredSupplierId ?? "none"}:${r.warehouseId}`;
      const g = map.get(key) ?? { key, supplierId: r.preferredSupplierId, supplierName: r.preferredSupplierName ?? "No preferred supplier", warehouseId: r.warehouseId, warehouseCode: r.warehouseCode, rows: [], value: 0 };
      g.rows.push(r);
      g.value += r.suggestedQuantity * (r.supplierUnitCost ?? 0);
      map.set(key, g);
    }
    return [...map.values()];
  }, [rows, selected]);

  const toggle = (r: ReorderSuggestion) =>
    setSelected((s) => {
      const next = new Set(s);
      if (next.has(rowKey(r))) next.delete(rowKey(r));
      else next.add(rowKey(r));
      return next;
    });
  const allSelected = rows.length > 0 && rows.every((r) => selected.has(rowKey(r)));

  const createPo = (g: Group) => {
    setPoPrefill({
      supplier: g.supplierId ? { id: g.supplierId, code: "", name: g.supplierName } : null,
      warehouseId: g.warehouseId,
      // unitCost null → the server applies the supplier's negotiated cost (shown as the placeholder).
      lines: g.rows.map((r) => ({ product: { id: r.productId, sku: r.sku, name: r.productName }, quantity: r.suggestedQuantity, unitCost: null })),
    });
    router.push("/purchase-orders/new");
  };

  return (
    <>
      <PageHeader
        title="Reorder suggestions"
        breadcrumbs={[{ label: "Purchase orders", href: "/purchase-orders" }, { label: "Suggestions" }]}
        description="Products at or below their reorder level, after counting what is already on order. Quantities are rounded up to the supplier's minimum order quantity."
      />

      {groups.length > 0 && (
        <Card className="mb-6 border-brand-600/30">
          <CardHeader title="Selected for ordering" description="One purchase order per supplier and receiving warehouse" />
          <CardBody>
            <ul className="divide-y divide-slate-100">
              {groups.map((g) => (
                <li key={g.key} className="flex flex-wrap items-center justify-between gap-3 py-2.5">
                  <div className="min-w-0">
                    <p className="font-medium text-slate-900">
                      {g.supplierName} <span className="font-mono text-xs text-slate-500">→ {g.warehouseCode}</span>
                    </p>
                    <p className="text-xs text-slate-500">
                      {g.rows.length} line{g.rows.length === 1 ? "" : "s"}
                      {g.value > 0 ? ` · est. ${formatCurrency(g.value, currency)}` : ""}
                      {!g.supplierId && " · choose a supplier on the next screen"}
                    </p>
                  </div>
                  <Button size="sm" icon={<ClipboardList className="size-3.5" />} onClick={() => createPo(g)}>
                    Create PO
                  </Button>
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>
      )}

      <Card>
        <Toolbar>
          <FilterSelect
            label="Warehouse"
            value={warehouseId}
            onChange={(v) => {
              setWarehouseId(v);
              setSelected(new Set());
            }}
            allLabel="All warehouses"
            options={(warehouses.data ?? []).map((w) => ({ value: w.id, label: `${w.code} — ${w.name}` }))}
          />
          {selected.size > 0 && (
            <Button variant="ghost" size="sm" onClick={() => setSelected(new Set())}>
              Clear selection ({selected.size})
            </Button>
          )}
        </Toolbar>
        <QueryBoundary
          query={query}
          columns={9}
          isEmpty={(d) => d.length === 0}
          empty={<EmptyState icon={<CheckCircle2 />} title="Nothing to reorder" description="Every product is above its reorder level once open purchase orders are counted." />}
        >
          {() => (
            <Table>
              <THead>
                <tr>
                  <TH className="w-10">
                    <input
                      type="checkbox"
                      aria-label="Select all suggestions"
                      checked={allSelected}
                      onChange={(e) => setSelected(e.target.checked ? new Set(rows.map(rowKey)) : new Set())}
                      className="size-4 accent-brand-700"
                    />
                  </TH>
                  <TH>Product</TH>
                  <TH>Warehouse</TH>
                  <TH align="right">Available</TH>
                  <TH align="right">Reorder level</TH>
                  <TH align="right">On order</TH>
                  <TH align="right">Suggested</TH>
                  <TH>Preferred supplier</TH>
                  <TH align="right">Unit cost</TH>
                  <TH align="right">Est. value</TH>
                </tr>
              </THead>
              <TBody>
                {rows.map((r) => {
                  const checked = selected.has(rowKey(r));
                  return (
                    <TR key={rowKey(r)} className={checked ? "bg-brand-50/50" : undefined}>
                      <TD>
                        <input type="checkbox" aria-label={`Select ${r.productName} at ${r.warehouseCode}`} checked={checked} onChange={() => toggle(r)} className="size-4 accent-brand-700" />
                      </TD>
                      <TD className="max-w-64">
                        <Link href={`/products/${r.productId}`} className="block truncate font-medium text-slate-900 hover:text-brand-700 hover:underline">
                          {r.productName}
                        </Link>
                        <span className="font-mono text-xs text-slate-500">{r.sku}</span>
                      </TD>
                      <TD className="font-mono text-xs">{r.warehouseCode}</TD>
                      <TD numeric className={r.quantityAvailable <= 0 ? "font-semibold text-red-600" : "font-medium text-amber-700"}>
                        {formatQty(r.quantityAvailable)}
                      </TD>
                      <TD numeric className="text-slate-500">
                        {formatQty(r.reorderLevel)}
                      </TD>
                      <TD numeric className={r.quantityOnOrder > 0 ? "text-slate-900" : "text-slate-400"}>
                        {formatQty(r.quantityOnOrder)}
                      </TD>
                      <TD numeric className="font-semibold text-slate-900">
                        {formatQty(r.suggestedQuantity)}
                      </TD>
                      <TD>
                        {r.preferredSupplierName ? (
                          r.preferredSupplierId ? (
                            <Link href={`/suppliers/${r.preferredSupplierId}`} className="text-slate-800 hover:text-brand-700 hover:underline">
                              {r.preferredSupplierName}
                            </Link>
                          ) : (
                            r.preferredSupplierName
                          )
                        ) : (
                          <Badge tone="neutral">
                            <Lightbulb className="size-3" aria-hidden /> None set
                          </Badge>
                        )}
                      </TD>
                      <TD numeric>{r.supplierUnitCost != null ? formatCurrency(r.supplierUnitCost, currency, { precise: true }) : "—"}</TD>
                      <TD numeric className="text-slate-600">
                        {r.supplierUnitCost != null ? formatCurrency(r.suggestedQuantity * r.supplierUnitCost, currency) : "—"}
                      </TD>
                    </TR>
                  );
                })}
              </TBody>
            </Table>
          )}
        </QueryBoundary>
      </Card>
    </>
  );
}
