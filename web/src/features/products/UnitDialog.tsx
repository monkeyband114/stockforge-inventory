"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Checkbox } from "@/components/ui/Checkbox";
import { Dialog } from "@/components/ui/Dialog";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { useToast } from "@/components/ui/Toast";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { qk } from "@/lib/queries";
import type { CreateUnitRequest, Unit, UpdateUnitRequest } from "@/lib/types";

export function UnitDialog({ open, onClose, unit }: { open: boolean; onClose: () => void; unit: Unit | null }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const editing = !!unit;
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [allowsFractions, setAllowsFractions] = useState(false);
  const [isActive, setIsActive] = useState(true);
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!open) return;
    setCode(unit?.code ?? "");
    setName(unit?.name ?? "");
    setAllowsFractions(unit?.allowsFractions ?? false);
    setIsActive(unit?.isActive ?? true);
    setErrors({});
    setFormError(null);
  }, [open, unit]);

  const submit = async () => {
    const v: Record<string, string | undefined> = {};
    if (!editing) {
      if (!code.trim()) v.code = "Code is required.";
      else if (!/^[A-Za-z0-9]+$/.test(code.trim())) v.code = "Code may contain only letters and digits.";
      else if (code.trim().length > 10) v.code = "Code must be at most 10 characters.";
    }
    if (!name.trim()) v.name = "Name is required.";
    else if (name.trim().length > 60) v.name = "Name must be at most 60 characters.";
    setErrors(v);
    if (Object.values(v).some(Boolean)) return;

    setSaving(true);
    setFormError(null);
    try {
      if (unit) {
        const body: UpdateUnitRequest = { name: name.trim(), allowsFractions, isActive };
        await api.put<Unit>(`/api/units/${unit.id}`, body);
        toast.success("Unit updated", `${unit.code} — ${name.trim()}`);
      } else {
        const body: CreateUnitRequest = { code: code.trim().toUpperCase(), name: name.trim(), allowsFractions };
        await api.post<Unit>("/api/units", body);
        toast.success("Unit created", `${code.trim().toUpperCase()} — ${name.trim()}`);
      }
      void queryClient.invalidateQueries({ queryKey: qk.units });
      void queryClient.invalidateQueries({ queryKey: ["products"] });
      onClose();
    } catch (err) {
      setErrors(fieldErrors(err));
      setFormError(err instanceof ApiError ? err.detail : "Saving failed.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      busy={saving}
      size="sm"
      title={editing ? `Edit unit ${unit?.code}` : "New unit of measure"}
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Cancel
          </Button>
          <Button type="submit" loading={saving}>
            {editing ? "Save changes" : "Create unit"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Input
          label="Code"
          value={code}
          onChange={(e) => setCode(e.target.value.toUpperCase())}
          error={errors.code}
          required={!editing}
          disabled={editing}
          maxLength={10}
          className="font-mono uppercase"
          hint={editing ? "Codes cannot be changed." : "Short symbol, e.g. EA, BOX, KG"}
        />
        <Input label="Name" value={name} onChange={(e) => setName(e.target.value)} error={errors.name} required maxLength={60} />
        <Checkbox
          label="Allows fractions"
          description="Enable for measured units (kg, litres). Countable units like boxes should stay whole."
          checked={allowsFractions}
          onChange={(e) => setAllowsFractions(e.target.checked)}
        />
        {editing && <Checkbox label="Active" description="Inactive units cannot be assigned to products." checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />}
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}
