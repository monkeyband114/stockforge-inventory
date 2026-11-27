"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Checkbox } from "@/components/ui/Checkbox";
import { Dialog } from "@/components/ui/Dialog";
import { FormError } from "@/components/ui/Field";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Textarea } from "@/components/ui/Textarea";
import { useToast } from "@/components/ui/Toast";
import { api, ApiError, fieldErrors } from "@/lib/api";
import { qk } from "@/lib/queries";
import type { Category, CreateCategoryRequest, UpdateCategoryRequest } from "@/lib/types";

/** Ids of the category and all of its descendants (cannot be chosen as its parent). */
function descendantsOf(id: string, all: Category[]): Set<string> {
  const out = new Set<string>([id]);
  let grew = true;
  while (grew) {
    grew = false;
    for (const c of all) {
      if (c.parentId && out.has(c.parentId) && !out.has(c.id)) {
        out.add(c.id);
        grew = true;
      }
    }
  }
  return out;
}

export function CategoryDialog({
  open,
  onClose,
  category,
  categories,
}: {
  open: boolean;
  onClose: () => void;
  /** Edit when set, create otherwise. */
  category: Category | null;
  categories: Category[];
}) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const editing = !!category;
  const [name, setName] = useState("");
  const [code, setCode] = useState("");
  const [description, setDescription] = useState("");
  const [parentId, setParentId] = useState("");
  const [isActive, setIsActive] = useState(true);
  const [errors, setErrors] = useState<Record<string, string | undefined>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!open) return;
    setName(category?.name ?? "");
    setCode(category?.code ?? "");
    setDescription(category?.description ?? "");
    setParentId(category?.parentId ?? "");
    setIsActive(category?.isActive ?? true);
    setErrors({});
    setFormError(null);
  }, [open, category]);

  const parentOptions = useMemo(() => {
    const excluded = category ? descendantsOf(category.id, categories) : new Set<string>();
    return categories.filter((c) => !excluded.has(c.id)).map((c) => ({ value: c.id, label: `${c.name} (${c.code})` }));
  }, [categories, category]);

  const submit = async () => {
    const v: Record<string, string | undefined> = {};
    if (!name.trim()) v.name = "Name is required.";
    else if (name.trim().length > 120) v.name = "Name must be at most 120 characters.";
    if (!editing && !/^[A-Za-z0-9]{2,6}$/.test(code.trim())) v.code = "Code must be 2–6 letters or digits.";
    if (description.length > 1000) v.description = "Description must be at most 1000 characters.";
    setErrors(v);
    if (Object.values(v).some(Boolean)) return;

    setSaving(true);
    setFormError(null);
    try {
      if (category) {
        const body: UpdateCategoryRequest = { name: name.trim(), description: description.trim() || null, parentId: parentId || null, isActive };
        await api.put<Category>(`/api/categories/${category.id}`, body);
        toast.success("Category updated", name.trim());
      } else {
        const body: CreateCategoryRequest = { name: name.trim(), code: code.trim().toUpperCase(), description: description.trim() || null, parentId: parentId || null };
        await api.post<Category>("/api/categories", body);
        toast.success("Category created", name.trim());
      }
      void queryClient.invalidateQueries({ queryKey: qk.categories });
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
      title={editing ? `Edit ${category?.name}` : "New category"}
      description={editing ? undefined : "Categories group products for reporting; the code prefixes auto-generated SKUs."}
      onSubmit={submit}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Cancel
          </Button>
          <Button type="submit" loading={saving}>
            {editing ? "Save changes" : "Create category"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-[1fr_9rem]">
          <Input label="Name" value={name} onChange={(e) => setName(e.target.value)} error={errors.name} required maxLength={120} />
          <Input
            label="Code"
            value={code}
            onChange={(e) => setCode(e.target.value.toUpperCase())}
            error={errors.code}
            required={!editing}
            disabled={editing}
            maxLength={6}
            className="font-mono uppercase"
            hint={editing ? "Codes cannot be changed." : "2–6 letters/digits"}
          />
        </div>
        <Select label="Parent category" value={parentId} onChange={(e) => setParentId(e.target.value)} error={errors.parentId} placeholder="None (top level)" options={parentOptions} />
        <Textarea label="Description" value={description} onChange={(e) => setDescription(e.target.value)} error={errors.description} maxLength={1000} rows={3} />
        {editing && <Checkbox label="Active" description="Inactive categories are hidden when assigning products." checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />}
        <FormError message={formError} />
      </div>
    </Dialog>
  );
}
