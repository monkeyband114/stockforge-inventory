// Product form model: string-backed state, validation mirroring CatalogContracts.cs / Catalog.cs, DTO mapping.

import { newIdempotencyKey } from "@/lib/api";
import type { CreateProductRequest, Product, ProductUnitRequest, UpdateProductRequest } from "@/lib/types";

export interface UnitRow {
  key: string;
  unitId: string;
  conversionFactor: string;
  barcode: string;
}

export interface ProductFormState {
  sku: string;
  name: string;
  description: string;
  categoryId: string;
  baseUnitId: string;
  barcode: string;
  generateBarcode: boolean;
  costPrice: string;
  sellingPrice: string;
  taxRatePercent: string;
  reorderLevel: string;
  reorderQuantity: string;
  isActive: boolean;
  units: UnitRow[];
}

export type FormErrors = Record<string, string | undefined>;

export function emptyUnitRow(): UnitRow {
  return { key: newIdempotencyKey(), unitId: "", conversionFactor: "", barcode: "" };
}

export function emptyProductForm(): ProductFormState {
  return {
    sku: "",
    name: "",
    description: "",
    categoryId: "",
    baseUnitId: "",
    barcode: "",
    generateBarcode: false,
    costPrice: "0",
    sellingPrice: "0",
    taxRatePercent: "",
    reorderLevel: "0",
    reorderQuantity: "0",
    isActive: true,
    units: [],
  };
}

/** Fraction → percent string without float noise (0.075 → "7.5"). */
function fractionToPercent(fraction: number | null): string {
  if (fraction === null || fraction === undefined) return "";
  return String(Number((fraction * 100).toFixed(3)));
}

export function productToForm(p: Product): ProductFormState {
  return {
    sku: p.sku,
    name: p.name,
    description: p.description ?? "",
    categoryId: p.categoryId ?? "",
    baseUnitId: p.baseUnitId,
    barcode: p.barcode ?? "",
    generateBarcode: false,
    costPrice: String(p.costPrice),
    sellingPrice: String(p.sellingPrice),
    taxRatePercent: fractionToPercent(p.taxRate),
    reorderLevel: String(p.reorderLevel),
    reorderQuantity: String(p.reorderQuantity),
    isActive: p.isActive,
    units: p.units.map((u) => ({
      key: newIdempotencyKey(),
      unitId: u.unitId,
      conversionFactor: String(u.conversionFactor),
      barcode: u.barcode ?? "",
    })),
  };
}

const SKU_RE = /^[A-Za-z0-9._-]*$/;
const BARCODE_RE = /^[A-Za-z0-9.-]*$/;

function decimals(value: string): number {
  const i = value.indexOf(".");
  return i < 0 ? 0 : value.length - i - 1;
}

function num(value: string): number {
  return value.trim() === "" ? Number.NaN : Number(value.trim());
}

function moneyError(value: string, label: string, scale: number): string | undefined {
  const n = num(value);
  if (Number.isNaN(n)) return `${label} is required.`;
  if (!Number.isFinite(n)) return `${label} must be a number.`;
  if (n < 0) return `${label} cannot be negative.`;
  if (n >= 1e14) return `${label} is too large.`;
  if (decimals(value.trim()) > scale) return `${label} can have at most ${scale} decimal places.`;
  return undefined;
}

function ean13Valid(code: string): boolean {
  const digits = code.split("").map(Number);
  const sum = digits.slice(0, 12).reduce((acc, d, i) => acc + d * (i % 2 === 0 ? 1 : 3), 0);
  return (10 - (sum % 10)) % 10 === digits[12];
}

/** Mirrors Barcodes.Normalize: 4–64 chars of letters/digits/'-'/'.', and a valid EAN-13 check digit for 13 digits. */
export function barcodeError(value: string, label = "Barcode"): string | undefined {
  const v = value.trim();
  if (!v) return undefined;
  if (!BARCODE_RE.test(v)) return `${label} may contain only letters, digits, '-' and '.'.`;
  if (v.length < 4 || v.length > 64) return `${label} must be 4–64 characters.`;
  if (/^\d{13}$/.test(v) && !ean13Valid(v)) return "This EAN-13 barcode has an invalid check digit.";
  return undefined;
}

export function validateProductForm(form: ProductFormState, mode: "create" | "edit"): FormErrors {
  const errors: FormErrors = {};
  if (mode === "create") {
    const sku = form.sku.trim();
    if (sku.length > 64) errors.sku = "SKU must be at most 64 characters.";
    else if (!SKU_RE.test(sku)) errors.sku = "SKU may contain letters, digits, '-', '_' and '.'.";
    if (!form.baseUnitId) errors.baseUnitId = "Choose the base unit of measure.";
  }
  if (!form.name.trim()) errors.name = "Name is required.";
  else if (form.name.trim().length > 200) errors.name = "Name must be at most 200 characters.";
  if (form.description.length > 4000) errors.description = "Description must be at most 4000 characters.";
  if (!(mode === "create" && form.generateBarcode)) errors.barcode = barcodeError(form.barcode);

  errors.costPrice = moneyError(form.costPrice, "Cost price", 4);
  errors.sellingPrice = moneyError(form.sellingPrice, "Selling price", 2);

  if (form.taxRatePercent.trim() !== "") {
    const t = num(form.taxRatePercent);
    if (!Number.isFinite(t)) errors.taxRate = "Tax rate must be a number.";
    else if (t < 0 || t > 100) errors.taxRate = "Tax rate must be between 0% and 100%.";
    else if (decimals(form.taxRatePercent.trim()) > 3) errors.taxRate = "Tax rate can have at most 3 decimal places.";
  }

  errors.reorderLevel = moneyError(form.reorderLevel, "Reorder level", 3);
  errors.reorderQuantity = moneyError(form.reorderQuantity, "Reorder quantity", 3);

  const seen = new Set<string>();
  const barcodes = new Set<string>(form.barcode.trim() && !form.generateBarcode ? [form.barcode.trim()] : []);
  form.units.forEach((u, i) => {
    if (!u.unitId) errors[`units[${i}].unitId`] = "Choose a unit.";
    else if (u.unitId === form.baseUnitId) errors[`units[${i}].unitId`] = "The base unit cannot be an alternate unit.";
    else if (seen.has(u.unitId)) errors[`units[${i}].unitId`] = "Each unit may be used only once.";
    seen.add(u.unitId);

    const f = num(u.conversionFactor);
    if (Number.isNaN(f)) errors[`units[${i}].conversionFactor`] = "Conversion factor is required.";
    else if (!Number.isFinite(f) || f <= 0) errors[`units[${i}].conversionFactor`] = "Must be greater than zero.";
    else if (decimals(u.conversionFactor.trim()) > 6) errors[`units[${i}].conversionFactor`] = "At most 6 decimal places.";

    const b = u.barcode.trim();
    const be = barcodeError(b);
    if (be) errors[`units[${i}].barcode`] = be;
    else if (b && barcodes.has(b)) errors[`units[${i}].barcode`] = "Barcodes must be unique.";
    if (b) barcodes.add(b);
  });

  for (const key of Object.keys(errors)) if (!errors[key]) delete errors[key];
  return errors;
}

function unitsPayload(form: ProductFormState): ProductUnitRequest[] {
  return form.units.map((u) => ({
    unitId: u.unitId,
    conversionFactor: Number(u.conversionFactor.trim()),
    barcode: u.barcode.trim() || null,
  }));
}

function taxFraction(form: ProductFormState): number | null {
  if (form.taxRatePercent.trim() === "") return null;
  // Percent → fraction, rounded to the column scale (numeric(6,5)) to avoid float artefacts.
  return Number((Number(form.taxRatePercent.trim()) / 100).toFixed(5));
}

export function toCreateRequest(form: ProductFormState): CreateProductRequest {
  return {
    sku: form.sku.trim() || null,
    name: form.name.trim(),
    description: form.description.trim() || null,
    categoryId: form.categoryId || null,
    baseUnitId: form.baseUnitId,
    barcode: form.generateBarcode ? null : form.barcode.trim() || null,
    generateBarcode: form.generateBarcode,
    costPrice: Number(form.costPrice.trim()),
    sellingPrice: Number(form.sellingPrice.trim()),
    taxRate: taxFraction(form),
    reorderLevel: Number(form.reorderLevel.trim()),
    reorderQuantity: Number(form.reorderQuantity.trim()),
    units: unitsPayload(form),
  };
}

function sameUnits(a: ProductUnitRequest[], b: ProductUnitRequest[]): boolean {
  const norm = (list: ProductUnitRequest[]) =>
    JSON.stringify([...list].map((u) => [u.unitId, Number(u.conversionFactor), u.barcode ?? null]).sort((x, y) => String(x[0]).localeCompare(String(y[0]))));
  return norm(a) === norm(b);
}

export function toUpdateRequest(form: ProductFormState, version: number, original?: Product): UpdateProductRequest {
  const units = unitsPayload(form);
  const originalUnits = original?.units.map((u) => ({ unitId: u.unitId, conversionFactor: u.conversionFactor, barcode: u.barcode })) ?? null;
  return {
    name: form.name.trim(),
    description: form.description.trim() || null,
    categoryId: form.categoryId || null,
    barcode: form.barcode.trim() || null,
    costPrice: Number(form.costPrice.trim()),
    sellingPrice: Number(form.sellingPrice.trim()),
    taxRate: taxFraction(form),
    reorderLevel: Number(form.reorderLevel.trim()),
    reorderQuantity: Number(form.reorderQuantity.trim()),
    isActive: form.isActive,
    // null leaves alternate units untouched; send the list only when the user changed it ([] removes all).
    // This also avoids `unit_inactive` when an existing assignment uses a since-deactivated unit.
    units: originalUnits && sameUnits(units, originalUnits) ? null : units,
    version,
  };
}

/** Full update request built from the current product (used for activate/deactivate). */
export function updateFromProduct(p: Product, overrides: Partial<UpdateProductRequest> = {}): UpdateProductRequest {
  return {
    name: p.name,
    description: p.description,
    categoryId: p.categoryId,
    barcode: p.barcode,
    costPrice: p.costPrice,
    sellingPrice: p.sellingPrice,
    taxRate: p.taxRate,
    reorderLevel: p.reorderLevel,
    reorderQuantity: p.reorderQuantity,
    isActive: p.isActive,
    // Activate/deactivate never touches alternate units.
    units: null,
    version: p.version,
    ...overrides,
  };
}
