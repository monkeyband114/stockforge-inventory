import type { HTMLAttributes, ReactNode, TdHTMLAttributes, ThHTMLAttributes } from "react";
import { cn } from "@/lib/cn";

/** Scroll container with a sticky header. */
export function Table({ children, className, maxHeight }: { children: ReactNode; className?: string; maxHeight?: string }) {
  return (
    <div className={cn("relative w-full overflow-auto", className)} style={maxHeight ? { maxHeight } : undefined}>
      <table className="w-full border-separate border-spacing-0 text-sm">{children}</table>
    </div>
  );
}

export function THead({ children }: { children: ReactNode }) {
  return <thead className="sticky top-0 z-10">{children}</thead>;
}

export function TBody({ children }: { children: ReactNode }) {
  return <tbody className="[&>tr:last-child>td]:border-b-0">{children}</tbody>;
}

export function TR({ children, className, interactive, ...props }: HTMLAttributes<HTMLTableRowElement> & { interactive?: boolean }) {
  return (
    <tr className={cn("group transition-colors hover:bg-slate-50/80", interactive && "cursor-pointer", className)} {...props}>
      {children}
    </tr>
  );
}

export function TH({ children, className, align = "left", ...props }: ThHTMLAttributes<HTMLTableCellElement> & { align?: "left" | "right" | "center" }) {
  return (
    <th
      scope="col"
      className={cn(
        "whitespace-nowrap border-b border-slate-200 bg-slate-50 px-4 py-2.5 text-xs font-semibold uppercase tracking-wide text-slate-500",
        align === "right" ? "text-right" : align === "center" ? "text-center" : "text-left",
        className,
      )}
      {...props}
    >
      {children}
    </th>
  );
}

export function TD({
  children,
  className,
  align = "left",
  numeric,
  ...props
}: TdHTMLAttributes<HTMLTableCellElement> & { align?: "left" | "right" | "center"; numeric?: boolean }) {
  const a = numeric ? "right" : align;
  return (
    <td
      className={cn(
        "border-b border-slate-100 px-4 py-2.5 align-middle text-slate-700",
        a === "right" ? "text-right" : a === "center" ? "text-center" : "text-left",
        numeric && "whitespace-nowrap tabular-nums",
        className,
      )}
      {...props}
    >
      {children}
    </td>
  );
}
