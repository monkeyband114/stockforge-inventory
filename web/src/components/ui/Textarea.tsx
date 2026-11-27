import type { ReactNode, TextareaHTMLAttributes } from "react";
import { cn } from "@/lib/cn";
import { controlClasses, Field } from "./Field";

export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: ReactNode;
  hint?: ReactNode;
  error?: string;
  containerClassName?: string;
}

export function Textarea({ label, hint, error, required, containerClassName, className, rows = 3, ...props }: TextareaProps) {
  return (
    <Field label={label} hint={hint} error={error} required={required} className={containerClassName}>
      {(control) => (
        <textarea {...control} required={required} rows={rows} className={cn(controlClasses, "py-2", className)} {...props} />
      )}
    </Field>
  );
}
