"use client";

import type { UseQueryResult } from "@tanstack/react-query";
import { Search } from "lucide-react";
import type { ReactNode } from "react";
import { EmptyState } from "@/components/ui/EmptyState";
import { ErrorState } from "@/components/ui/ErrorState";
import { controlClasses } from "@/components/ui/Field";
import { TableSkeleton } from "@/components/ui/Skeleton";
import { cn } from "@/lib/cn";

/**
 * Renders loading skeleton → error (with retry) → empty state → content for a query.
 * Background refetches keep showing the previous data.
 */
export function QueryBoundary<T>({
  query,
  isEmpty,
  empty,
  skeleton,
  columns = 6,
  rows = 8,
  children,
}: {
  query: UseQueryResult<T>;
  isEmpty?: (data: T) => boolean;
  empty?: ReactNode;
  skeleton?: ReactNode;
  columns?: number;
  rows?: number;
  children: (data: T) => ReactNode;
}) {
  if (query.isPending) return <>{skeleton ?? <TableSkeleton columns={columns} rows={rows} />}</>;
  if (query.isError && query.data === undefined) return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  const data = query.data as T;
  if (isEmpty?.(data)) return <>{empty ?? <EmptyState title="Nothing here yet" />}</>;
  return <>{children(data)}</>;
}

export function SearchInput({
  value,
  onChange,
  placeholder = "Search…",
  label = "Search",
  className,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  label?: string;
  className?: string;
}) {
  return (
    <div className={cn("relative min-w-48 flex-1 sm:max-w-xs", className)}>
      <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-400" aria-hidden />
      <input
        type="search"
        aria-label={label}
        placeholder={placeholder}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className={cn(controlClasses, "h-9 pl-9")}
      />
    </div>
  );
}

/** Compact inline select for filter bars (label visually hidden but accessible). */
export function FilterSelect({
  label,
  value,
  onChange,
  options,
  allLabel,
  className,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: { value: string; label: string }[];
  allLabel?: string;
  className?: string;
}) {
  return (
    <label className={cn("relative flex flex-col", className)}>
      <span className="sr-only">{label}</span>
      <select
        aria-label={label}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className={cn(controlClasses, "h-9 min-w-40 appearance-none pr-8")}
      >
        {allLabel !== undefined && <option value="">{allLabel}</option>}
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
      <svg className="pointer-events-none absolute right-2.5 top-1/2 size-4 -translate-y-1/2 text-slate-400" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
        <path fillRule="evenodd" d="M5.23 7.21a.75.75 0 0 1 1.06.02L10 11.17l3.71-3.94a.75.75 0 1 1 1.08 1.04l-4.25 4.5a.75.75 0 0 1-1.08 0l-4.25-4.5a.75.75 0 0 1 .02-1.06Z" clipRule="evenodd" />
      </svg>
    </label>
  );
}

/** Empty state for filtered lists with a "clear filters" action. */
export function NoResults({ onClear, what = "records" }: { onClear?: () => void; what?: string }) {
  return (
    <EmptyState
      icon={<Search />}
      title={`No ${what} match your filters`}
      description="Try a different search term or remove some filters."
      action={
        onClear ? (
          <button type="button" onClick={onClear} className="text-sm font-medium text-brand-700 hover:text-brand-600">
            Clear filters
          </button>
        ) : undefined
      }
    />
  );
}
