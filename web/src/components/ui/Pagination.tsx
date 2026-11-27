import { ChevronLeft, ChevronRight } from "lucide-react";
import { formatInteger } from "@/lib/format";
import { Button } from "./Button";

export function Pagination({
  page,
  pageSize,
  totalCount,
  totalPages,
  onPageChange,
  onPageSizeChange,
  pageSizes = [25, 50, 100],
}: {
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  onPageSizeChange?: (size: number) => void;
  pageSizes?: number[];
}) {
  if (totalCount === 0) return null;
  const from = (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalCount);
  return (
    <nav className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-200 px-4 py-3 text-sm text-slate-600" aria-label="Pagination">
      <p className="tabular-nums">
        Showing <span className="font-medium text-slate-900">{formatInteger(from)}</span>–
        <span className="font-medium text-slate-900">{formatInteger(to)}</span> of{" "}
        <span className="font-medium text-slate-900">{formatInteger(totalCount)}</span>
      </p>
      <div className="flex items-center gap-3">
        {onPageSizeChange && (
          <label className="flex items-center gap-2 text-xs text-slate-500">
            Rows
            <select
              className="h-8 rounded-md border border-slate-300 bg-white px-2 text-sm text-slate-700"
              value={pageSize}
              onChange={(e) => onPageSizeChange(Number(e.target.value))}
            >
              {pageSizes.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>
        )}
        <div className="flex items-center gap-1">
          <Button variant="secondary" size="icon-sm" disabled={page <= 1} onClick={() => onPageChange(page - 1)} aria-label="Previous page">
            <ChevronLeft className="size-4" />
          </Button>
          <span className="min-w-20 text-center text-xs tabular-nums text-slate-500">
            Page {page} of {Math.max(totalPages, 1)}
          </span>
          <Button
            variant="secondary"
            size="icon-sm"
            disabled={page >= totalPages}
            onClick={() => onPageChange(page + 1)}
            aria-label="Next page"
          >
            <ChevronRight className="size-4" />
          </Button>
        </div>
      </div>
    </nav>
  );
}
