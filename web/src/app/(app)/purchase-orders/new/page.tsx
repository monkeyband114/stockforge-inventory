"use client";

import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import { PageHeader } from "@/components/ui/PageHeader";
import { PageSpinner } from "@/components/ui/Spinner";
import { RequirePermission } from "@/features/common/RequirePermission";
import { clearPoPrefill, peekPoPrefill } from "@/features/procurement/poPrefill";
import { PurchaseOrderForm } from "@/features/procurement/PurchaseOrderForm";
import { toPickedSupplier } from "@/features/procurement/SupplierPicker";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { P } from "@/lib/permissions";
import { qk } from "@/lib/queries";
import type { Supplier } from "@/lib/types";

export default function NewPurchaseOrderPage() {
  return (
    <RequirePermission anyOf={[P.purchasingCreate]}>
      <Suspense fallback={<PageSpinner />}>
        <NewPurchaseOrderView />
      </Suspense>
    </RequirePermission>
  );
}

function NewPurchaseOrderView() {
  const params = useSearchParams();
  const { hasPermission } = useAuth();
  const [prefill] = useState(() => peekPoPrefill());
  useEffect(() => clearPoPrefill(), []);
  const supplierId = prefill ? null : params.get("supplierId");
  const supplier = useQuery({
    queryKey: qk.supplier(supplierId ?? ""),
    queryFn: () => api.get<Supplier>(`/api/suppliers/${supplierId}`),
    enabled: !!supplierId && hasPermission(P.suppliersView),
  });

  const fromSuggestions = !!prefill?.lines.length;
  return (
    <>
      <PageHeader
        title="New purchase order"
        description={fromSuggestions ? `Pre-filled with ${prefill!.lines.length} reorder suggestion${prefill!.lines.length === 1 ? "" : "s"} — review quantities and costs before saving.` : undefined}
        breadcrumbs={[{ label: "Purchase orders", href: "/purchase-orders" }, { label: "New" }]}
      />
      {supplierId && supplier.isPending ? (
        <PageSpinner label="Loading supplier…" />
      ) : (
        <PurchaseOrderForm
          prefill={
            prefill ?? (supplier.data?.isActive ? { supplier: toPickedSupplier(supplier.data), warehouseId: params.get("warehouseId") ?? "", lines: [] } : null)
          }
        />
      )}
    </>
  );
}
