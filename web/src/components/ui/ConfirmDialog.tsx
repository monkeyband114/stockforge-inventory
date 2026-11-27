"use client";

import { useEffect, useState, type ReactNode } from "react";
import { Button, type ButtonVariant } from "./Button";
import { Dialog } from "./Dialog";
import { FormError } from "./Field";
import { Textarea } from "./Textarea";

export interface ConfirmDialogProps {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  description?: ReactNode;
  confirmLabel?: string;
  tone?: "primary" | "danger";
  /** Shows a notes/reason textarea; its value is passed to onConfirm. */
  notes?: { label: string; required?: boolean; placeholder?: string; maxLength?: number };
  onConfirm: (notes: string) => Promise<unknown> | void;
  children?: ReactNode;
}

/** Confirmation for state-changing actions. Stays open with the API error if the action fails. */
export function ConfirmDialog({
  open,
  onClose,
  title,
  description,
  confirmLabel = "Confirm",
  tone = "primary",
  notes,
  onConfirm,
  children,
}: ConfirmDialogProps) {
  const [value, setValue] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [touched, setTouched] = useState(false);

  useEffect(() => {
    if (open) {
      setValue("");
      setError(null);
      setTouched(false);
    }
  }, [open]);

  const notesMissing = !!notes?.required && value.trim().length === 0;

  const submit = async () => {
    setTouched(true);
    if (notesMissing) return;
    setBusy(true);
    setError(null);
    try {
      await onConfirm(value.trim());
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : "The action failed.");
    } finally {
      setBusy(false);
    }
  };

  const variant: ButtonVariant = tone === "danger" ? "danger" : "primary";

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={title}
      description={description}
      size="sm"
      busy={busy}
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button type="submit" variant={variant} loading={busy} data-autofocus={notes ? undefined : true}>
            {confirmLabel}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {children}
        {notes && (
          <Textarea
            label={notes.label}
            required={notes.required}
            placeholder={notes.placeholder}
            maxLength={notes.maxLength ?? 1000}
            value={value}
            onChange={(e) => setValue(e.target.value)}
            error={touched && notesMissing ? `${notes.label} is required.` : undefined}
          />
        )}
        <FormError message={error} />
      </div>
    </Dialog>
  );
}
