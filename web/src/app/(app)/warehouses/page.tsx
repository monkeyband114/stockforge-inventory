"use client";

import { MapPin, Pencil, Plus, Warehouse as WarehouseIcon } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { ToggleChip } from "@/components/ui/Checkbox";
import { Card } from "@/components/ui/Card";
import { EmptyState } from "@/components/ui/EmptyState";
import { PageHeader, Toolbar } from "@/components/ui/PageHeader";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { NoResults, QueryBoundary, SearchInput } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { ActiveBadge } from "@/features/common/StatusBadges";
import { WarehouseFormDialog, warehouseTypeLabel } from "@/features/warehouses/WarehouseFormDialog";
import { useAuth } from "@/lib/auth";
import { formatDate } from "@/lib/format";
import { P } from "@/lib/permissions";
import { useWarehouses } from "@/lib/queries";
import type { Warehouse } from "@/lib/types";

export default function WarehousesPage() {
  return (
    <RequirePermission anyOf={[P.warehousesView]}>
      <WarehousesView />
    </RequirePermission>
  );
}

function WarehousesView() {
  const { hasPermission, canAccessWarehouse } = useAuth();
  const canManage = hasPermission(P.warehousesManage);
  const [includeInactive, setIncludeInactive] = useState(false);
  const [search, setSearch] = useState("");
  const [editing, setEditing] = useState<Warehouse | null>(null);
  const [creating, setCreating] = useState(false);
  const query = useWarehouses(includeInactive);

  const term = search.trim().toLowerCase();
  const filter = (list: Warehouse[]) =>
    term
      ? list.filter((w) => [w.code, w.name, w.address?.city, w.address?.country].some((v) => v?.toLowerCase().includes(term)))
      : list;

  return (
    <>
      <PageHeader
        title="Warehouses"
        description="Stocking locations in your organization. Open a warehouse to see its live dashboard."
        actions={
          canManage ? (
            <Button icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
              New warehouse
            </Button>
          ) : null
        }
      />
      <Card>
        <Toolbar>
          <SearchInput value={search} onChange={setSearch} placeholder="Search code, name or city" label="Search warehouses" />
          {canManage && (
            <ToggleChip checked={includeInactive} onChange={setIncludeInactive}>
              Show inactive
            </ToggleChip>
          )}
        </Toolbar>
        <QueryBoundary
          query={query}
          columns={6}
          isEmpty={(list) => filter(list).length === 0}
          empty={
            term ? (
              <NoResults onClear={() => setSearch("")} what="warehouses" />
            ) : (
              <EmptyState
                icon={<WarehouseIcon />}
                title="No warehouses yet"
                description="Create your first warehouse to start receiving stock."
                action={
                  canManage ? (
                    <Button size="sm" icon={<Plus className="size-4" />} onClick={() => setCreating(true)}>
                      New warehouse
                    </Button>
                  ) : undefined
                }
              />
            )
          }
        >
          {(list) => (
            <Table>
              <THead>
                <tr>
                  <TH>Code</TH>
                  <TH>Name</TH>
                  <TH>Type</TH>
                  <TH>Location</TH>
                  <TH>Contact</TH>
                  <TH>Status</TH>
                  <TH>Created</TH>
                  {canManage && (
                    <TH>
                      <span className="sr-only">Actions</span>
                    </TH>
                  )}
                </tr>
              </THead>
              <TBody>
                {filter(list).map((w) => {
                  const accessible = canAccessWarehouse(w.id);
                  return (
                    <TR key={w.id}>
                      <TD className="font-mono text-xs font-semibold text-slate-900">{w.code}</TD>
                      <TD>
                        {accessible ? (
                          <Link href={`/warehouses/${w.id}`} className="font-medium text-slate-900 hover:text-brand-700 hover:underline">
                            {w.name}
                          </Link>
                        ) : (
                          <span className="font-medium text-slate-700">{w.name}</span>
                        )}
                        {!accessible && (
                          <Badge tone="neutral" className="ml-2">
                            Not assigned
                          </Badge>
                        )}
                      </TD>
                      <TD>{warehouseTypeLabel(w.type)}</TD>
                      <TD>
                        {w.address ? (
                          <span className="inline-flex items-center gap-1 text-slate-600">
                            <MapPin className="size-3.5 text-slate-400" aria-hidden />
                            {[w.address.city, w.address.country].filter(Boolean).join(", ")}
                          </span>
                        ) : (
                          <span className="text-slate-400">—</span>
                        )}
                      </TD>
                      <TD className="text-xs text-slate-600">
                        {w.phone && <div>{w.phone}</div>}
                        {w.email && <div className="text-slate-500">{w.email}</div>}
                        {!w.phone && !w.email && <span className="text-slate-400">—</span>}
                      </TD>
                      <TD>
                        <div className="flex flex-wrap gap-1">
                          <ActiveBadge active={w.isActive} />
                          {w.isActive && !w.canOperate && <Badge tone="warning">Not operational</Badge>}
                        </div>
                      </TD>
                      <TD className="whitespace-nowrap text-slate-500">{formatDate(w.createdAt)}</TD>
                      {canManage && (
                        <TD align="right">
                          <Button variant="ghost" size="icon-sm" onClick={() => setEditing(w)} aria-label={`Edit ${w.name}`}>
                            <Pencil className="size-4" />
                          </Button>
                        </TD>
                      )}
                    </TR>
                  );
                })}
              </TBody>
            </Table>
          )}
        </QueryBoundary>
      </Card>
      <WarehouseFormDialog open={creating} onClose={() => setCreating(false)} />
      <WarehouseFormDialog open={!!editing} onClose={() => setEditing(null)} warehouse={editing} />
    </>
  );
}
