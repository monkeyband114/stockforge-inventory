import { ChevronDown } from "lucide-react";
import type { ReactNode, SelectHTMLAttributes } from "react";
import { cn } from "@/lib/cn";
import { controlClasses, Field } from "./Field";

export interface SelectOption {
  value: string;
  label: string;
  disabled?: boolean;
}

export interface SelectProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, "size"> {
  label?: ReactNode;
  hint?: ReactNode;
  error?: string;
  options: SelectOption[];
  /** Adds a first option with an empty value (e.g. "All warehouses"). */
  placeholder?: string;
  containerClassName?: string;
}

export function Select({ label, hint, error, required, options, placeholder, containerClassName, className, ...props }: SelectProps) {
  return (
    <Field label={label} hint={hint} error={error} required={required} className={containerClassName}>
      {(control) => (
        <div className="relative">
          <select {...control} required={required} className={cn(controlClasses, "h-9 appearance-none pr-9", className)} {...props}>
            {placeholder !== undefined && <option value="">{placeholder}</option>}
            {options.map((o) => (
              <option key={o.value} value={o.value} disabled={o.disabled}>
                {o.label}
              </option>
            ))}
          </select>
          <ChevronDown className="pointer-events-none absolute right-2.5 top-1/2 size-4 -translate-y-1/2 text-slate-400" aria-hidden />
        </div>
      )}
    </Field>
  );
}
