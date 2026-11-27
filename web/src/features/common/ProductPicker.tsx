"use client";

import { Package, Search, X } from "lucide-react";
import { useEffect, useId, useRef, useState } from "react";
import { controlClasses, Field } from "@/components/ui/Field";
import { Spinner } from "@/components/ui/Spinner";
import { cn } from "@/lib/cn";
import { formatQty } from "@/lib/format";
import { useDebouncedValue } from "@/lib/hooks";
import { useProductSearch } from "@/lib/queries";
import type { ProductListItem } from "@/lib/types";

export interface PickedProduct {
  id: string;
  sku: string;
  name: string;
  baseUnitCode?: string;
}

/** Accessible combobox searching active products by name, SKU or barcode. */
export function ProductPicker({
  value,
  onChange,
  label = "Product",
  error,
  required,
  excludeIds,
  disabled,
  hint,
}: {
  value: PickedProduct | null;
  onChange: (product: PickedProduct | null) => void;
  label?: string;
  error?: string;
  required?: boolean;
  excludeIds?: string[];
  disabled?: boolean;
  hint?: string;
}) {
  const [query, setQuery] = useState("");
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const debounced = useDebouncedValue(query, 250);
  const rootRef = useRef<HTMLDivElement>(null);
  const listId = useId();
  const search = useProductSearch(debounced, open && !value);
  const results: ProductListItem[] = (search.data?.items ?? []).filter((p) => !excludeIds?.includes(p.id));

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  useEffect(() => setActive(0), [debounced]);

  const pick = (p: ProductListItem) => {
    onChange({ id: p.id, sku: p.sku, name: p.name, baseUnitCode: p.baseUnitCode });
    setQuery("");
    setOpen(false);
  };

  if (value) {
    return (
      <Field label={label} error={error} required={required} hint={hint}>
        {(control) => (
          <div
            {...control}
            className={cn(controlClasses, "flex h-9 items-center gap-2", disabled && "bg-slate-50")}
            role="group"
            aria-label={`${label}: ${value.name}`}
          >
            <Package className="size-4 shrink-0 text-slate-400" aria-hidden />
            <span className="min-w-0 flex-1 truncate">
              <span className="font-medium text-slate-900">{value.name}</span>
              <span className="ml-2 font-mono text-xs text-slate-500">{value.sku}</span>
            </span>
            {!disabled && (
              <button
                type="button"
                onClick={() => onChange(null)}
                className="rounded p-0.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                aria-label="Clear selected product"
              >
                <X className="size-4" />
              </button>
            )}
          </div>
        )}
      </Field>
    );
  }

  return (
    <Field label={label} error={error} required={required} hint={hint}>
      {(control) => (
        <div ref={rootRef} className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-400" aria-hidden />
          <input
            {...control}
            type="text"
            role="combobox"
            aria-expanded={open}
            aria-controls={listId}
            aria-autocomplete="list"
            aria-activedescendant={open && results[active] ? `${listId}-${results[active].id}` : undefined}
            disabled={disabled}
            placeholder="Search by name, SKU or scan barcode"
            className={cn(controlClasses, "h-9 pl-9")}
            value={query}
            onChange={(e) => {
              setQuery(e.target.value);
              setOpen(true);
            }}
            onFocus={() => setOpen(true)}
            onKeyDown={(e) => {
              if (e.key === "ArrowDown") {
                e.preventDefault();
                setOpen(true);
                setActive((a) => Math.min(a + 1, results.length - 1));
              } else if (e.key === "ArrowUp") {
                e.preventDefault();
                setActive((a) => Math.max(a - 1, 0));
              } else if (e.key === "Enter") {
                if (open && results[active]) {
                  e.preventDefault();
                  pick(results[active]);
                }
              } else if (e.key === "Escape" && open) {
                e.stopPropagation();
                setOpen(false);
              }
            }}
          />
          {open && (
            <ul
              id={listId}
              role="listbox"
              aria-label="Matching products"
              className="absolute z-50 mt-1 max-h-64 w-full overflow-y-auto rounded-lg border border-slate-200 bg-white py-1 shadow-lg"
            >
              {search.isLoading ? (
                <li className="flex items-center gap-2 px-3 py-2 text-sm text-slate-500">
                  <Spinner /> Searching…
                </li>
              ) : results.length === 0 ? (
                <li className="px-3 py-2 text-sm text-slate-500">{search.isError ? "Search failed." : "No matching active products."}</li>
              ) : (
                results.map((p, i) => (
                  <li
                    key={p.id}
                    id={`${listId}-${p.id}`}
                    role="option"
                    aria-selected={i === active}
                    onMouseDown={(e) => {
                      e.preventDefault();
                      pick(p);
                    }}
                    onMouseEnter={() => setActive(i)}
                    className={cn("flex cursor-pointer items-center justify-between gap-3 px-3 py-2 text-sm", i === active ? "bg-brand-50" : "hover:bg-slate-50")}
                  >
                    <span className="min-w-0">
                      <span className="block truncate font-medium text-slate-900">{p.name}</span>
                      <span className="block font-mono text-xs text-slate-500">{p.sku}</span>
                    </span>
                    <span className="shrink-0 text-xs tabular-nums text-slate-500">
                      {formatQty(p.quantityAvailable)} {p.baseUnitCode} avail.
                    </span>
                  </li>
                ))
              )}
            </ul>
          )}
        </div>
      )}
    </Field>
  );
}
