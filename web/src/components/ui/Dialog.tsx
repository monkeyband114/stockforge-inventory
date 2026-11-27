"use client";

import { X } from "lucide-react";
import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { cn } from "@/lib/cn";
import { useFocusTrap } from "./useFocusTrap";

export interface DialogProps {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  description?: ReactNode;
  children?: ReactNode;
  footer?: ReactNode;
  size?: "sm" | "md" | "lg" | "xl";
  /** Wraps the body + footer in a <form> so Enter submits. */
  onSubmit?: (event: FormEvent<HTMLFormElement>) => void;
  /** Prevent closing (e.g. while a request is in flight). */
  busy?: boolean;
}

const widths = { sm: "max-w-md", md: "max-w-lg", lg: "max-w-2xl", xl: "max-w-4xl" };

export function Portal({ children }: { children: ReactNode }) {
  const [mounted, setMounted] = useState(false);
  useEffect(() => setMounted(true), []);
  return mounted ? createPortal(children, document.body) : null;
}

export function Dialog({ open, onClose, title, description, children, footer, size = "md", onSubmit, busy }: DialogProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const descId = useId();
  const close = () => {
    if (!busy) onClose();
  };
  useFocusTrap(panelRef, open, close);

  if (!open) return null;

  const body = (
    <>
      <div className="max-h-[calc(100dvh-12rem)] overflow-y-auto px-5 py-4">{children}</div>
      {footer && <div className="flex flex-wrap items-center justify-end gap-2 border-t border-slate-200 bg-slate-50/70 px-5 py-3">{footer}</div>}
    </>
  );

  return (
    <Portal>
      <div data-overlay-root className="fixed inset-0 z-50 flex items-end justify-center p-0 sm:items-center sm:p-4">
        <div className="absolute inset-0 animate-fade-in bg-slate-900/50 backdrop-blur-[1px]" aria-hidden="true" onMouseDown={close} />
        <div
          ref={panelRef}
          role="dialog"
          aria-modal="true"
          aria-labelledby={titleId}
          aria-describedby={description ? descId : undefined}
          tabIndex={-1}
          className={cn(
            "relative flex w-full animate-slide-up flex-col overflow-hidden rounded-t-xl bg-white shadow-2xl ring-1 ring-slate-900/5 outline-none sm:rounded-xl",
            widths[size],
          )}
        >
          <div className="flex items-start justify-between gap-4 border-b border-slate-200 px-5 py-4">
            <div className="min-w-0">
              <h2 id={titleId} className="text-base font-semibold text-slate-900">
                {title}
              </h2>
              {description && (
                <p id={descId} className="mt-1 text-sm text-slate-500">
                  {description}
                </p>
              )}
            </div>
            <button
              type="button"
              onClick={close}
              disabled={busy}
              className="-mr-1 rounded-md p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600 disabled:opacity-50"
              aria-label="Close dialog"
            >
              <X className="size-5" />
            </button>
          </div>
          {onSubmit ? (
            <form
              noValidate
              onSubmit={(e) => {
                e.preventDefault();
                onSubmit(e);
              }}
              className="flex min-h-0 flex-col"
            >
              {body}
            </form>
          ) : (
            body
          )}
        </div>
      </div>
    </Portal>
  );
}
