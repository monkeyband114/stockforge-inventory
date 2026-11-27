"use client";

import { X } from "lucide-react";
import { useId, useRef, type ReactNode } from "react";
import { cn } from "@/lib/cn";
import { Portal } from "./Dialog";
import { useFocusTrap } from "./useFocusTrap";

export interface DrawerProps {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  subtitle?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  side?: "right" | "left";
  width?: string;
}

export function Drawer({ open, onClose, title, subtitle, children, footer, side = "right", width = "max-w-xl" }: DrawerProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  useFocusTrap(panelRef, open, onClose);
  if (!open) return null;

  return (
    <Portal>
      <div data-overlay-root className="fixed inset-0 z-50 flex">
        <div className="absolute inset-0 animate-fade-in bg-slate-900/40" aria-hidden="true" onMouseDown={onClose} />
        <div
          ref={panelRef}
          role="dialog"
          aria-modal="true"
          aria-labelledby={titleId}
          tabIndex={-1}
          className={cn(
            "relative flex h-full w-full flex-col bg-white shadow-2xl outline-none",
            width,
            side === "right" ? "ml-auto animate-slide-in-right" : "mr-auto animate-slide-in-left",
          )}
        >
          <div className="flex items-start justify-between gap-4 border-b border-slate-200 px-5 py-4">
            <div className="min-w-0">
              <h2 id={titleId} className="truncate text-base font-semibold text-slate-900">
                {title}
              </h2>
              {subtitle && <div className="mt-0.5 text-sm text-slate-500">{subtitle}</div>}
            </div>
            <button
              type="button"
              onClick={onClose}
              className="-mr-1 rounded-md p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
              aria-label="Close panel"
            >
              <X className="size-5" />
            </button>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">{children}</div>
          {footer && <div className="flex flex-wrap items-center justify-end gap-2 border-t border-slate-200 bg-slate-50/70 px-5 py-3">{footer}</div>}
        </div>
      </div>
    </Portal>
  );
}
