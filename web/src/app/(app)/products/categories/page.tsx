"use client";

import { useQueryClient } from "@tanstack/react-query";
import { FolderTree, MoreHorizontal, Pencil, Plus, Ruler, Trash2 } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useMemo, useState } from "react";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { Card, CardHeader } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { EmptyState } from "@/components/ui/EmptyState";
import { Menu } from "@/components/ui/Menu";
import { PageHeader } from "@/components/ui/PageHeader";
import { PageSpinner } from "@/components/ui/Spinner";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/Table";
import { TabPanel, Tabs } from "@/components/ui/Tabs";
import { useToast } from "@/components/ui/Toast";
import { QueryBoundary } from "@/features/common/QueryBoundary";
import { RequirePermission } from "@/features/common/RequirePermission";
import { ActiveBadge } from "@/features/common/StatusBadges";
import { CategoryDialog } from "@/features/products/CategoryDialog";
import { UnitDialog } from "@/features/products/UnitDialog";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatInteger } from "@/lib/format";
import { P } from "@/lib/permissions";
import { qk, useCategories, useUnits } from "@/lib/queries";
import type { Category, Unit } from "@/lib/types";

type Tab = "categories" | "units";
const TAB_ID = "catalog-settings";

/** Depth-first ordering so sub-categories appear under their parent. */
function asTree(categories: Category[]): { category: Category; depth: number }[] {
  const byParent = new Map<string | null, Category[]>();
  const ids = new Set(categories.map((c) => c.id));
  for (const c of categories) {
    const key = c.parentId && ids.has(c.parentId) ? c.parentId : null;
    byParent.set(key, [...(byParent.get(key) ?? []), c]);
  }
  const out: { category: Category; depth: number }[] = [];
  const visit = (parent: string | null, depth: number) => {
    for (const c of (byParent.get(parent) ?? []).sort((a, b) => a.name.localeCompare(b.name))) {
      out.push({ category: c, depth });
      if (depth < 10) visit(c.id, depth + 1);
    }
  };
  visit(null, 0);
  return out;
}

function CategoriesSection({ canManage }: { canManage: boolean }) {
  const query = useCategories();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [editing, setEditing] = useState<Category | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [deleting, setDeleting] = useState<Category | null>(null);
  const names = useMemo(() => new Map((query.data ?? []).map((c) => [c.id, c.name])), [query.data]);
  const tree = useMemo(() => asTree(query.data ?? []), [query.data]);

  return (
    <Card>
      <CardHeader
        title="Product categories"
        description="Hierarchical grouping for products, reports and SKU prefixes."
        actions={
          canManage ? (
            <Button
              size="sm"
              icon={<Plus className="size-3.5" />}
              onClick={() => {
                setEditing(null);
                setDialogOpen(true);
              }}
            >
              New category
            </Button>
          ) : undefined
        }
      />
      <QueryBoundary
        query={query}
        columns={6}
        rows={6}
        isEmpty={(d) => d.length === 0}
        empty={<EmptyState icon={<FolderTree />} title="No categories yet" description="Create categories to organise your catalog." />}
      >
        {() => (
          <Table>
            <THead>
              <tr>
                <TH>Name</TH>
                <TH>Code</TH>
                <TH>Parent</TH>
                <TH>Description</TH>
                <TH align="right">Products</TH>
                <TH>Status</TH>
                {canManage && (
                  <TH align="right">
                    <span className="sr-only">Actions</span>
                  </TH>
                )}
              </tr>
            </THead>
            <TBody>
              {tree.map(({ category: c, depth }) => (
                <TR key={c.id}>
                  <TD className="font-medium text-slate-900">
                    <span style={{ paddingLeft: depth * 18 }} className="inline-flex items-center gap-1.5">
                      {depth > 0 && <span className="text-slate-300" aria-hidden>└</span>}
                      {c.name}
                    </span>
                  </TD>
                  <TD className="font-mono text-xs">{c.code}</TD>
                  <TD>{c.parentId ? names.get(c.parentId) ?? "—" : <span className="text-slate-400">—</span>}</TD>
                  <TD className="max-w-xs truncate text-slate-500" title={c.description ?? undefined}>
                    {c.description ?? <span className="text-slate-400">—</span>}
                  </TD>
                  <TD numeric>
                    {c.productCount > 0 ? (
                      <Link href={`/products?categoryId=${c.id}`} className="font-medium text-brand-700 hover:underline">
                        {formatInteger(c.productCount)}
                      </Link>
                    ) : (
                      <span className="text-slate-400">0</span>
                    )}
                  </TD>
                  <TD>
                    <ActiveBadge active={c.isActive} />
                  </TD>
                  {canManage && (
                    <TD align="right">
                      <Menu
                        label={`Actions for ${c.name}`}
                        trigger={(props) => (
                          <Button {...props} variant="ghost" size="icon-sm" aria-label={`Actions for ${c.name}`}>
                            <MoreHorizontal className="size-4" />
                          </Button>
                        )}
                        items={[
                          {
                            label: "Edit",
                            icon: <Pencil />,
                            onSelect: () => {
                              setEditing(c);
                              setDialogOpen(true);
                            },
                          },
                          { label: "Delete", icon: <Trash2 />, tone: "danger", onSelect: () => setDeleting(c) },
                        ]}
                      />
                    </TD>
                  )}
                </TR>
              ))}
            </TBody>
          </Table>
        )}
      </QueryBoundary>

      {canManage && (
        <>
          <CategoryDialog open={dialogOpen} onClose={() => setDialogOpen(false)} category={editing} categories={query.data ?? []} />
          <ConfirmDialog
            open={!!deleting}
            onClose={() => setDeleting(null)}
            title={`Delete category ${deleting?.name ?? ""}?`}
            description={
              deleting && deleting.productCount > 0
                ? `This category still has ${deleting.productCount} product(s). Reassign them first, or deactivate the category instead.`
                : "Categories without products or sub-categories can be deleted. This cannot be undone."
            }
            confirmLabel="Delete category"
            tone="danger"
            onConfirm={async () => {
              if (!deleting) return;
              await api.delete(`/api/categories/${deleting.id}`);
              toast.success("Category deleted", deleting.name);
              void queryClient.invalidateQueries({ queryKey: qk.categories });
              void queryClient.invalidateQueries({ queryKey: ["products"] });
            }}
          />
        </>
      )}
    </Card>
  );
}

function UnitsSection({ canManage }: { canManage: boolean }) {
  const query = useUnits();
  const [editing, setEditing] = useState<Unit | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);

  return (
    <Card>
      <CardHeader
        title="Units of measure"
        description="Base and packaging units used by products. Codes are permanent."
        actions={
          canManage ? (
            <Button
              size="sm"
              icon={<Plus className="size-3.5" />}
              onClick={() => {
                setEditing(null);
                setDialogOpen(true);
              }}
            >
              New unit
            </Button>
          ) : undefined
        }
      />
      <QueryBoundary
        query={query}
        columns={4}
        rows={6}
        isEmpty={(d) => d.length === 0}
        empty={<EmptyState icon={<Ruler />} title="No units yet" description="Create units such as EA, BOX or KG." />}
      >
        {(units) => (
          <Table>
            <THead>
              <tr>
                <TH>Code</TH>
                <TH>Name</TH>
                <TH>Quantities</TH>
                <TH>Status</TH>
                {canManage && (
                  <TH align="right">
                    <span className="sr-only">Actions</span>
                  </TH>
                )}
              </tr>
            </THead>
            <TBody>
              {[...units]
                .sort((a, b) => a.code.localeCompare(b.code))
                .map((u) => (
                  <TR key={u.id}>
                    <TD className="font-mono font-medium text-slate-900">{u.code}</TD>
                    <TD>{u.name}</TD>
                    <TD>{u.allowsFractions ? <Badge tone="info">Fractional</Badge> : <Badge tone="neutral">Whole units</Badge>}</TD>
                    <TD>
                      <ActiveBadge active={u.isActive} />
                    </TD>
                    {canManage && (
                      <TD align="right">
                        <Button
                          variant="ghost"
                          size="sm"
                          icon={<Pencil className="size-3.5" />}
                          aria-label={`Edit unit ${u.code}`}
                          onClick={() => {
                            setEditing(u);
                            setDialogOpen(true);
                          }}
                        >
                          Edit
                        </Button>
                      </TD>
                    )}
                  </TR>
                ))}
            </TBody>
          </Table>
        )}
      </QueryBoundary>
      {canManage && <UnitDialog open={dialogOpen} onClose={() => setDialogOpen(false)} unit={editing} />}
    </Card>
  );
}

function CatalogSettingsView() {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(P.productsManage);
  const params = useSearchParams();
  const router = useRouter();
  const initial: Tab = params.get("tab") === "units" ? "units" : "categories";
  const [tab, setTab] = useState<Tab>(initial);
  const categories = useCategories();
  const units = useUnits();

  const change = (next: Tab) => {
    setTab(next);
    router.replace(next === "units" ? "/products/categories?tab=units" : "/products/categories", { scroll: false });
  };

  return (
    <>
      <PageHeader
        title="Catalog settings"
        description="Manage the categories and units of measure used across your product catalog."
        breadcrumbs={[{ label: "Products", href: "/products" }, { label: "Catalog settings" }]}
      />
      <Tabs<Tab>
        idBase={TAB_ID}
        className="mb-5"
        value={tab}
        onChange={change}
        tabs={[
          { value: "categories", label: "Categories", count: categories.data?.length },
          { value: "units", label: "Units of measure", count: units.data?.length },
        ]}
      />
      <TabPanel idBase={TAB_ID} value={tab}>
        {tab === "categories" ? <CategoriesSection canManage={canManage} /> : <UnitsSection canManage={canManage} />}
      </TabPanel>
    </>
  );
}

export default function CatalogSettingsPage() {
  return (
    <RequirePermission anyOf={[P.productsView]}>
      <Suspense fallback={<PageSpinner />}>
        <CatalogSettingsView />
      </Suspense>
    </RequirePermission>
  );
}
