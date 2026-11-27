"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useAuth } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { Logo } from "./Logo";
import { visibleNav, type NavItem } from "./nav";

function isActive(pathname: string, item: NavItem): boolean {
  if (item.exact) return pathname === item.href || (item.href === "/products" && /^\/products\/(?!categories)[^/]+$/.test(pathname));
  return pathname === item.href || pathname.startsWith(`${item.href}/`);
}

export function SidebarContent({ onNavigate }: { onNavigate?: () => void }) {
  const pathname = usePathname();
  const { user } = useAuth();
  const groups = visibleNav(user?.permissions ?? [], !!user?.tenant);

  return (
    <div className="flex h-full flex-col bg-navy-900 text-slate-300">
      <div className="flex h-14 shrink-0 items-center border-b border-white/5 px-4">
        <Link href="/" onClick={onNavigate} className="rounded-md focus-visible:outline-white">
          <Logo dark />
        </Link>
      </div>
      <nav className="flex-1 overflow-y-auto px-3 py-4" aria-label="Main navigation">
        {groups.map((group) => (
          <div key={group.label} className="mb-5">
            <p className="mb-1.5 px-2.5 text-[11px] font-semibold uppercase tracking-wider text-slate-500">{group.label}</p>
            <ul className="space-y-0.5">
              {group.items.map((item) => {
                const active = isActive(pathname, item);
                const Icon = item.icon;
                return (
                  <li key={`${group.label}-${item.href}`}>
                    <Link
                      href={item.href}
                      onClick={onNavigate}
                      aria-current={active ? "page" : undefined}
                      className={cn(
                        "group flex items-center gap-3 rounded-md px-2.5 py-2 text-sm font-medium transition-colors focus-visible:outline-white",
                        active ? "bg-navy-800 text-white shadow-[inset_2px_0_0_0_#60A5FA]" : "text-slate-300 hover:bg-navy-800 hover:text-white",
                      )}
                    >
                      <Icon className={cn("size-4 shrink-0", active ? "text-blue-300" : "text-slate-400 group-hover:text-slate-200")} aria-hidden />
                      {item.label}
                    </Link>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </nav>
      {user?.tenant && (
        <div className="border-t border-white/5 px-4 py-3 text-xs text-slate-500">
          <p className="truncate">
            Signed in to <span className="text-slate-300">{user.tenant.name}</span>
          </p>
        </div>
      )}
    </div>
  );
}
