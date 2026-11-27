import { useId, type InputHTMLAttributes, type ReactNode } from "react";
import { cn } from "@/lib/cn";

export interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "type"> {
  label: ReactNode;
  description?: ReactNode;
}

export function Checkbox({ label, description, className, id, ...props }: CheckboxProps) {
  const generated = useId();
  const inputId = id ?? generated;
  return (
    <div className={cn("flex items-start gap-2.5", className)}>
      <input
        id={inputId}
        type="checkbox"
        className="mt-0.5 size-4 shrink-0 cursor-pointer rounded border-slate-300 accent-brand-700 disabled:cursor-not-allowed"
        aria-describedby={description ? `${inputId}-desc` : undefined}
        {...props}
      />
      <div className="min-w-0">
        <label htmlFor={inputId} className={cn("cursor-pointer text-sm text-slate-800", props.disabled && "cursor-not-allowed text-slate-500")}>
          {label}
        </label>
        {description && (
          <p id={`${inputId}-desc`} className="text-xs text-slate-500">
            {description}
          </p>
        )}
      </div>
    </div>
  );
}

/** Pill-style toggle used in filter bars. */
export function ToggleChip({
  checked,
  onChange,
  children,
}: {
  checked: boolean;
  onChange: (next: boolean) => void;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      onClick={() => onChange(!checked)}
      className={cn(
        "inline-flex h-9 items-center gap-2 rounded-md border px-3 text-sm font-medium transition-colors",
        checked ? "border-brand-600 bg-brand-50 text-brand-800" : "border-slate-300 bg-white text-slate-600 hover:bg-slate-50",
      )}
    >
      <span
        className={cn("size-2 rounded-full", checked ? "bg-brand-600" : "bg-slate-300")}
        aria-hidden="true"
      />
      {children}
    </button>
  );
}

/** Accessible on/off switch for settings forms. */
export function Switch({
  checked,
  onChange,
  label,
  description,
  disabled,
}: {
  checked: boolean;
  onChange: (next: boolean) => void;
  label: ReactNode;
  description?: ReactNode;
  disabled?: boolean;
}) {
  const id = useId();
  return (
    <div className="flex items-start justify-between gap-4">
      <div className="min-w-0">
        <span id={`${id}-label`} className="text-sm font-medium text-slate-800">
          {label}
        </span>
        {description && <p className="text-xs text-slate-500">{description}</p>}
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        aria-labelledby={`${id}-label`}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={cn(
          "relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors disabled:opacity-50",
          checked ? "bg-brand-700" : "bg-slate-300",
        )}
      >
        <span
          className={cn("inline-block size-4 rounded-full bg-white shadow transition-transform", checked ? "translate-x-4.5" : "translate-x-0.5")}
        />
      </button>
    </div>
  );
}
