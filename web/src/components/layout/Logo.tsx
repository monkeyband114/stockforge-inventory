import { cn } from "@/lib/cn";

export function LogoMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" className={cn("size-8", className)} aria-hidden="true">
      <rect width="32" height="32" rx="8" fill="#1D4ED8" />
      <path d="M8 11.5 16 7l8 4.5v9L16 25l-8-4.5v-9Z" fill="none" stroke="#fff" strokeWidth="2" strokeLinejoin="round" />
      <path d="M8 11.5 16 16l8-4.5M16 16v9" fill="none" stroke="#93C5FD" strokeWidth="2" strokeLinejoin="round" />
    </svg>
  );
}

export function Logo({ dark = false, className }: { dark?: boolean; className?: string }) {
  return (
    <span className={cn("inline-flex items-center gap-2.5", className)}>
      <LogoMark />
      <span className={cn("text-[15px] font-semibold tracking-tight", dark ? "text-white" : "text-slate-900")}>StockForge</span>
    </span>
  );
}
