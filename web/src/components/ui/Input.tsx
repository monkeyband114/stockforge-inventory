import type { InputHTMLAttributes, ReactNode } from "react";
import { cn } from "@/lib/cn";
import { controlClasses, Field } from "./Field";

export interface InputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "size"> {
  label?: ReactNode;
  hint?: ReactNode;
  error?: string;
  containerClassName?: string;
  /** Element rendered inside the control on the left (icon). */
  leading?: ReactNode;
  /** Element rendered inside the control on the right (unit, %). */
  trailing?: ReactNode;
  ref?: React.Ref<HTMLInputElement>;
}

export function Input({ label, hint, error, required, containerClassName, className, leading, trailing, ...props }: InputProps) {
  return (
    <Field label={label} hint={hint} error={error} required={required} className={containerClassName}>
      {(control) => (
        <div className="relative">
          {leading && (
            <span className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 text-slate-400">{leading}</span>
          )}
          <input
            {...control}
            required={required}
            className={cn(controlClasses, "h-9", leading ? "pl-9" : undefined, trailing ? "pr-10" : undefined, className)}
            {...props}
          />
          {trailing && (
            <span className="pointer-events-none absolute inset-y-0 right-0 flex items-center pr-3 text-xs text-slate-500">
              {trailing}
            </span>
          )}
        </div>
      )}
    </Field>
  );
}
