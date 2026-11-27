"use client";

import { useCallback, useState } from "react";
import { useDebouncedValue } from "@/lib/hooks";

/** Paging + search state for list pages; resetting to page 1 whenever a filter changes. */
export function useListParams<F extends Record<string, unknown>>(initialFilters: F, initialPageSize = 25, initialSearch = "") {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSizeState] = useState(initialPageSize);
  const [search, setSearchState] = useState(initialSearch);
  const [filters, setFiltersState] = useState<F>(initialFilters);
  const debouncedSearch = useDebouncedValue(search.trim(), 300);

  const setSearch = useCallback((value: string) => {
    setSearchState(value);
    setPage(1);
  }, []);

  const setFilter = useCallback(<K extends keyof F>(key: K, value: F[K]) => {
    setFiltersState((f) => ({ ...f, [key]: value }));
    setPage(1);
  }, []);

  const setPageSize = useCallback((size: number) => {
    setPageSizeState(size);
    setPage(1);
  }, []);

  /** Restores the initial filters and clears the search. */
  const reset = useCallback(() => {
    setFiltersState(initialFilters);
    setSearchState("");
    setPage(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return { page, setPage, pageSize, setPageSize, search, setSearch, debouncedSearch, filters, setFilter, reset };
}
