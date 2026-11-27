"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useMemo } from "react";
import { api } from "./api";
import { useAuth } from "./auth";
import { P } from "./permissions";
import type { Category, Organization, PagedResult, Product, ProductListItem, Unit, Warehouse } from "./types";

/** Query-key roots. Realtime events invalidate by root (see realtime.tsx). */
export const qk = {
  organization: ["organization"] as const,
  warehouses: (includeInactive = false) => ["warehouses", { includeInactive }] as const,
  warehouse: (id: string) => ["warehouses", "detail", id] as const,
  categories: ["categories"] as const,
  units: ["units"] as const,
  products: (params: object) => ["products", "list", params] as const,
  product: (id: string) => ["products", "detail", id] as const,
  inventory: (params: object) => ["inventory", params] as const,
  movements: (params: object) => ["movements", params] as const,
  adjustments: (params: object) => ["adjustments", params] as const,
  transfers: (params: object) => ["transfers", "list", params] as const,
  transfer: (id: string) => ["transfers", "detail", id] as const,
  reservations: (params: object) => ["reservations", params] as const,
  orgDashboard: (days: number) => ["dashboard", "organization", days] as const,
  warehouseDashboard: (id: string, days: number) => ["dashboard", "warehouse", id, days] as const,
  users: (params: object) => ["users", params] as const,
  roles: ["roles"] as const,
  permissions: ["permissions"] as const,
  audit: (params: object) => ["audit", params] as const,
  jobs: (params: object) => ["jobs", "list", params] as const,
  jobStats: ["jobs", "stats"] as const,
  sessions: ["sessions"] as const,
  platformStats: ["platform", "stats"] as const,
  tenants: (params: object) => ["platform", "tenants", params] as const,
};

export function useOrganization() {
  const { user, hasPermission } = useAuth();
  return useQuery({
    queryKey: qk.organization,
    queryFn: () => api.get<Organization>("/api/organization"),
    enabled: !!user?.tenant && hasPermission(P.organizationView),
    staleTime: 5 * 60_000,
  });
}

/** Tenant base currency for money formatting (falls back to USD until loaded). */
export function useCurrency(): string {
  const { data } = useOrganization();
  return data?.baseCurrency ?? "USD";
}

export function useWarehouses(includeInactive = false) {
  const { user, hasPermission } = useAuth();
  return useQuery({
    queryKey: qk.warehouses(includeInactive),
    queryFn: () => api.get<Warehouse[]>("/api/warehouses", { includeInactive }),
    enabled: !!user?.tenant && hasPermission(P.warehousesView),
    staleTime: 60_000,
  });
}

/** Active warehouses the member can operate on (assigned ones for warehouse-scoped staff). */
export function useAccessibleWarehouses() {
  const query = useWarehouses(false);
  const { canAccessWarehouse } = useAuth();
  const data = useMemo(() => query.data?.filter((w) => canAccessWarehouse(w.id)), [query.data, canAccessWarehouse]);
  return { ...query, data };
}

export function useCategories() {
  const { user, hasPermission } = useAuth();
  return useQuery({
    queryKey: qk.categories,
    queryFn: () => api.get<Category[]>("/api/categories"),
    enabled: !!user?.tenant && hasPermission(P.productsView),
    staleTime: 60_000,
  });
}

export function useUnits() {
  const { user, hasPermission } = useAuth();
  return useQuery({
    queryKey: qk.units,
    queryFn: () => api.get<Unit[]>("/api/units"),
    enabled: !!user?.tenant && hasPermission(P.productsView),
    staleTime: 60_000,
  });
}

export function useProduct(id: string | null | undefined) {
  return useQuery({
    queryKey: qk.product(id ?? ""),
    queryFn: () => api.get<Product>(`/api/products/${id}`),
    enabled: !!id,
  });
}

/** Product search for pickers (name, SKU or exact barcode). */
export function useProductSearch(search: string, enabled = true) {
  const params = { search: search.trim() || undefined, isActive: true, pageSize: 20, sort: "name" };
  return useQuery({
    queryKey: qk.products({ picker: true, ...params }),
    queryFn: ({ signal }) => api.get<PagedResult<ProductListItem>>("/api/products", params, signal),
    enabled,
    placeholderData: keepPreviousData,
    staleTime: 30_000,
  });
}
