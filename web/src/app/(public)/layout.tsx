import type { ReactNode } from "react";
import { Logo } from "@/components/layout/Logo";

export default function PublicLayout({ children }: { children: ReactNode }) {
  return (
    <div className="grid min-h-dvh lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)]">
      <aside className="relative hidden overflow-hidden bg-navy-900 lg:flex lg:flex-col lg:justify-between lg:p-12">
        <div
          className="pointer-events-none absolute inset-0 opacity-[0.07]"
          style={{
            backgroundImage: "linear-gradient(#fff 1px, transparent 1px), linear-gradient(90deg, #fff 1px, transparent 1px)",
            backgroundSize: "40px 40px",
          }}
          aria-hidden="true"
        />
        <Logo dark className="relative" />
        <div className="relative max-w-md">
          <h2 className="text-3xl font-semibold leading-tight tracking-tight text-white">Inventory you can trust, across every warehouse.</h2>
          <p className="mt-4 text-sm leading-relaxed text-slate-300">
            Real-time stock levels, audited movements, approvals and transfers for multi-site operations — with every change traceable.
          </p>
          <dl className="mt-8 grid grid-cols-3 gap-4 border-t border-white/10 pt-6 text-sm">
            <div>
              <dt className="text-slate-400">Ledger</dt>
              <dd className="mt-1 font-medium text-white">Append-only</dd>
            </div>
            <div>
              <dt className="text-slate-400">Updates</dt>
              <dd className="mt-1 font-medium text-white">Real-time</dd>
            </div>
            <div>
              <dt className="text-slate-400">Access</dt>
              <dd className="mt-1 font-medium text-white">Role-based</dd>
            </div>
          </dl>
        </div>
        <p className="relative text-xs text-slate-500">© {new Date().getFullYear()} StockForge</p>
      </aside>
      <main className="flex flex-col items-center justify-center bg-white px-6 py-12 sm:px-10">
        <div className="w-full max-w-sm">
          <Logo className="mb-10 lg:hidden" />
          {children}
        </div>
      </main>
    </div>
  );
}
