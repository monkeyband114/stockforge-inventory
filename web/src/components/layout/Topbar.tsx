"use client";

import { Bell, Building2, Check, ChevronDown, CircleUser, LogOut, Menu as MenuIcon } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { useToast } from "@/components/ui/Toast";
import { Spinner } from "@/components/ui/Spinner";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { formatRelative, fullName, initials } from "@/lib/format";
import { roleLabel } from "@/lib/permissions";
import { useRealtime, type NotificationTone } from "@/lib/realtime";

function useDismiss(open: boolean, close: () => void) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!ref.current?.contains(e.target as Node)) close();
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") close();
    };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open, close]);
  return ref;
}

function OrgSwitcher() {
  const { user, switchOrganization } = useAuth();
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [switching, setSwitching] = useState<string | null>(null);
  const ref = useDismiss(open, () => setOpen(false));

  if (!user) return null;
  if (!user.tenant) {
    return (
      <span className="inline-flex items-center gap-2 rounded-md px-2 py-1.5 text-sm font-medium text-slate-700">
        <Building2 className="size-4 text-slate-400" aria-hidden />
        Platform administration
      </span>
    );
  }

  const memberships = user.memberships;
  if (memberships.length <= 1) {
    return (
      <span className="inline-flex min-w-0 items-center gap-2 rounded-md px-2 py-1.5 text-sm font-medium text-slate-800">
        <Building2 className="size-4 shrink-0 text-slate-400" aria-hidden />
        <span className="truncate">{user.tenant.name}</span>
      </span>
    );
  }

  const choose = async (tenantId: string) => {
    if (tenantId === user.tenant?.id) {
      setOpen(false);
      return;
    }
    setSwitching(tenantId);
    try {
      await switchOrganization(tenantId);
      setOpen(false);
      const name = memberships.find((m) => m.id === tenantId)?.name;
      toast.success(`Switched to ${name ?? "organization"}`);
    } catch (e) {
      toast.error(e);
    } finally {
      setSwitching(null);
    }
  };

  return (
    <div ref={ref} className="relative min-w-0">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-label={`Current organization: ${user.tenant.name}. Switch organization`}
        className="inline-flex min-w-0 max-w-64 items-center gap-2 rounded-md border border-slate-200 bg-white px-2.5 py-1.5 text-sm font-medium text-slate-800 hover:bg-slate-50"
      >
        <Building2 className="size-4 shrink-0 text-slate-400" aria-hidden />
        <span className="truncate">{user.tenant.name}</span>
        <ChevronDown className="size-4 shrink-0 text-slate-400" aria-hidden />
      </button>
      {open && (
        <div className="absolute left-0 z-40 mt-1 w-72 animate-fade-in rounded-lg border border-slate-200 bg-white py-1 shadow-lg">
          <p className="px-3 py-1.5 text-[11px] font-semibold uppercase tracking-wider text-slate-500">Your organizations</p>
          <ul role="listbox" aria-label="Organizations">
            {memberships.map((m) => {
              const current = m.id === user.tenant?.id;
              return (
                <li key={m.id} role="option" aria-selected={current}>
                  <button
                    type="button"
                    disabled={!!switching}
                    onClick={() => choose(m.id)}
                    className="flex w-full items-center gap-3 px-3 py-2 text-left text-sm hover:bg-slate-50 focus:bg-slate-100 focus:outline-none disabled:opacity-60"
                  >
                    <span className="flex size-7 shrink-0 items-center justify-center rounded-md bg-slate-100 text-xs font-semibold text-slate-600">
                      {m.name.slice(0, 2).toUpperCase()}
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate font-medium text-slate-800">{m.name}</span>
                      <span className="block truncate text-xs text-slate-500">{m.slug}</span>
                    </span>
                    {switching === m.id ? <Spinner /> : current ? <Check className="size-4 text-brand-700" aria-label="Current" /> : null}
                  </button>
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </div>
  );
}

function LiveIndicator() {
  const { state } = useRealtime();
  const { user } = useAuth();
  if (!user?.tenant) return null;
  const config = {
    connected: { label: "Live", dot: "bg-emerald-500", text: "text-emerald-700", title: "Real-time updates connected" },
    connecting: { label: "Connecting", dot: "bg-amber-400 animate-pulse", text: "text-amber-700", title: "Connecting to real-time updates" },
    reconnecting: { label: "Reconnecting", dot: "bg-amber-400 animate-pulse", text: "text-amber-700", title: "Connection lost — reconnecting" },
    disconnected: { label: "Offline", dot: "bg-red-500", text: "text-red-700", title: "Real-time updates disconnected; data refreshes on navigation" },
    offline: { label: "Offline", dot: "bg-slate-400", text: "text-slate-500", title: "Real-time updates are not active" },
  }[state];
  return (
    <span
      className={cn("hidden items-center gap-1.5 rounded-full border border-slate-200 bg-white px-2.5 py-1 text-xs font-medium sm:inline-flex", config.text)}
      title={config.title}
      role="status"
      aria-live="polite"
    >
      <span className={cn("size-2 rounded-full", config.dot)} aria-hidden="true" />
      {config.label}
    </span>
  );
}

const toneDot: Record<NotificationTone, string> = {
  info: "bg-blue-500",
  success: "bg-emerald-500",
  warning: "bg-amber-500",
  danger: "bg-red-500",
};

function NotificationBell() {
  const { notifications, unreadCount, markAllRead, markRead, clearAll } = useRealtime();
  const { user } = useAuth();
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const ref = useDismiss(open, () => setOpen(false));
  if (!user?.tenant) return null;

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-haspopup="dialog"
        aria-label={unreadCount ? `Notifications, ${unreadCount} unread` : "Notifications"}
        className="relative rounded-md p-2 text-slate-500 hover:bg-slate-100 hover:text-slate-800"
      >
        <Bell className="size-5" />
        {unreadCount > 0 && (
          <span className="absolute right-1 top-1 flex min-w-4 items-center justify-center rounded-full bg-red-600 px-1 text-[10px] font-semibold leading-4 text-white tabular-nums">
            {unreadCount > 9 ? "9+" : unreadCount}
          </span>
        )}
      </button>
      {open && (
        <div
          role="dialog"
          aria-label="Notifications"
          className="absolute right-0 z-40 mt-1 w-[min(24rem,calc(100vw-1.5rem))] animate-fade-in overflow-hidden rounded-lg border border-slate-200 bg-white shadow-lg"
        >
          <div className="flex items-center justify-between border-b border-slate-200 px-4 py-2.5">
            <p className="text-sm font-semibold text-slate-900">Notifications</p>
            <div className="flex items-center gap-1">
              <button
                type="button"
                onClick={markAllRead}
                disabled={unreadCount === 0}
                className="rounded px-2 py-1 text-xs font-medium text-brand-700 hover:bg-brand-50 disabled:text-slate-400 disabled:hover:bg-transparent"
              >
                Mark all read
              </button>
              <button
                type="button"
                onClick={clearAll}
                disabled={notifications.length === 0}
                className="rounded px-2 py-1 text-xs font-medium text-slate-500 hover:bg-slate-100 disabled:text-slate-300 disabled:hover:bg-transparent"
              >
                Clear
              </button>
            </div>
          </div>
          {notifications.length === 0 ? (
            <div className="px-4 py-10 text-center">
              <Bell className="mx-auto size-6 text-slate-300" aria-hidden />
              <p className="mt-2 text-sm font-medium text-slate-700">You&apos;re all caught up</p>
              <p className="mt-0.5 text-xs text-slate-500">Low-stock alerts, approvals and transfer updates appear here in real time.</p>
            </div>
          ) : (
            <ul className="max-h-96 divide-y divide-slate-100 overflow-y-auto">
              {notifications.map((n) => (
                <li key={n.id}>
                  <button
                    type="button"
                    onClick={() => {
                      markRead(n.id);
                      setOpen(false);
                      if (n.href) router.push(n.href);
                    }}
                    className={cn("flex w-full gap-3 px-4 py-3 text-left hover:bg-slate-50", !n.read && "bg-brand-50/40")}
                  >
                    <span className={cn("mt-1.5 size-2 shrink-0 rounded-full", toneDot[n.tone], n.read && "opacity-40")} aria-hidden="true" />
                    <span className="min-w-0 flex-1">
                      <span className={cn("block text-sm", n.read ? "text-slate-600" : "font-medium text-slate-900")}>{n.title}</span>
                      <span className="mt-0.5 block text-xs text-slate-500">{n.message}</span>
                      <span className="mt-1 block text-[11px] text-slate-400">{formatRelative(n.at)}</span>
                    </span>
                    {!n.read && <span className="sr-only">Unread</span>}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

function UserMenu() {
  const { user, logout } = useAuth();
  const [open, setOpen] = useState(false);
  const [signingOut, setSigningOut] = useState(false);
  const ref = useDismiss(open, () => setOpen(false));
  if (!user) return null;

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-haspopup="menu"
        aria-label="Account menu"
        className="flex items-center gap-2 rounded-md p-1 pr-2 hover:bg-slate-100"
      >
        <span className="flex size-8 items-center justify-center rounded-full bg-navy-800 text-xs font-semibold text-white">{initials(user)}</span>
        <span className="hidden text-left md:block">
          <span className="block max-w-40 truncate text-sm font-medium leading-tight text-slate-800">{fullName(user)}</span>
          <span className="block max-w-40 truncate text-xs leading-tight text-slate-500">
            {user.roles.length ? user.roles.map(roleLabel).join(", ") : user.isPlatformAdmin ? "Super admin" : user.email}
          </span>
        </span>
        <ChevronDown className="hidden size-4 text-slate-400 md:block" aria-hidden />
      </button>
      {open && (
        <div role="menu" className="absolute right-0 z-40 mt-1 w-64 animate-fade-in rounded-lg border border-slate-200 bg-white py-1 shadow-lg">
          <div className="border-b border-slate-100 px-3 py-2.5">
            <p className="truncate text-sm font-medium text-slate-900">{fullName(user)}</p>
            <p className="truncate text-xs text-slate-500">{user.email}</p>
          </div>
          <Link
            href="/account"
            role="menuitem"
            onClick={() => setOpen(false)}
            className="flex items-center gap-2.5 px-3 py-2 text-sm text-slate-700 hover:bg-slate-50 focus:bg-slate-100 focus:outline-none"
          >
            <CircleUser className="size-4 text-slate-400" aria-hidden /> Account &amp; security
          </Link>
          <button
            type="button"
            role="menuitem"
            disabled={signingOut}
            onClick={async () => {
              setSigningOut(true);
              await logout();
            }}
            className="flex w-full items-center gap-2.5 px-3 py-2 text-left text-sm text-slate-700 hover:bg-slate-50 focus:bg-slate-100 focus:outline-none"
          >
            {signingOut ? <Spinner /> : <LogOut className="size-4 text-slate-400" aria-hidden />} Sign out
          </button>
        </div>
      )}
    </div>
  );
}

export function Topbar({ onOpenMenu }: { onOpenMenu: () => void }) {
  return (
    <header className="sticky top-0 z-30 flex h-14 shrink-0 items-center gap-3 border-b border-slate-200 bg-white/95 px-3 backdrop-blur sm:px-5">
      <button
        type="button"
        onClick={onOpenMenu}
        className="rounded-md p-2 text-slate-500 hover:bg-slate-100 hover:text-slate-800 lg:hidden"
        aria-label="Open navigation"
      >
        <MenuIcon className="size-5" />
      </button>
      <OrgSwitcher />
      <div className="flex-1" />
      <LiveIndicator />
      <NotificationBell />
      <UserMenu />
    </header>
  );
}
