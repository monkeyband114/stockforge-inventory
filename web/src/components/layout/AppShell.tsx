"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { PageSpinner } from "@/components/ui/Spinner";
import { Portal } from "@/components/ui/Dialog";
import { useFocusTrap } from "@/components/ui/useFocusTrap";
import { useAuth } from "@/lib/auth";
import { SidebarContent } from "./Sidebar";
import { Topbar } from "./Topbar";

function MobileDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  const ref = useRef<HTMLDivElement>(null);
  useFocusTrap(ref, open, onClose);
  if (!open) return null;
  return (
    <Portal>
      <div data-overlay-root className="fixed inset-0 z-50 lg:hidden">
        <div className="absolute inset-0 animate-fade-in bg-slate-900/50" onMouseDown={onClose} aria-hidden="true" />
        <div ref={ref} role="dialog" aria-modal="true" aria-label="Navigation" tabIndex={-1} className="relative h-full w-72 animate-slide-in-left shadow-2xl outline-none">
          <SidebarContent onNavigate={onClose} />
        </div>
      </div>
    </Portal>
  );
}

/** Authenticated application frame: guards the session, renders sidebar + top bar. */
export function AppShell({ children }: { children: ReactNode }) {
  const { status, user, sessionExpired } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const [menuOpen, setMenuOpen] = useState(false);
  const previousStatus = useRef(status);

  useEffect(() => {
    const wasSignedIn = previousStatus.current === "authenticated";
    previousStatus.current = status;
    if (status === "unauthenticated") {
      // An explicit sign-out already navigates to /login; only deep links and expired sessions keep `next`.
      if (wasSignedIn && !sessionExpired) return;
      const params = new URLSearchParams();
      if (pathname && pathname !== "/") params.set("next", pathname);
      if (sessionExpired) params.set("reason", "expired");
      const qs = params.toString();
      router.replace(qs ? `/login?${qs}` : "/login");
    }
  }, [status, sessionExpired, pathname, router]);

  useEffect(() => setMenuOpen(false), [pathname]);

  if (status !== "authenticated" || !user) {
    return <PageSpinner label={status === "loading" ? "Restoring your session…" : "Redirecting to sign in…"} />;
  }

  return (
    <div className="flex min-h-dvh">
      <a href="#main" className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-[70] focus:rounded-md focus:bg-white focus:px-3 focus:py-2 focus:text-sm focus:shadow">
        Skip to content
      </a>
      <aside className="fixed inset-y-0 left-0 hidden w-64 lg:block">
        <SidebarContent />
      </aside>
      <MobileDrawer open={menuOpen} onClose={() => setMenuOpen(false)} />
      <div className="flex min-w-0 flex-1 flex-col lg:pl-64">
        <Topbar onOpenMenu={() => setMenuOpen(true)} />
        {/* Keyed by organization so every page remounts (and drops local filter state) after a switch. */}
        <main id="main" key={user.tenant?.id ?? "platform"} className="mx-auto w-full max-w-[1600px] flex-1 px-4 py-6 sm:px-6 lg:px-8">
          {children}
        </main>
      </div>
    </div>
  );
}
