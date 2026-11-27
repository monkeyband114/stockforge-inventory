"use client";

import { ScanBarcode } from "lucide-react";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { controlClasses } from "@/components/ui/Field";
import { api, ApiError } from "@/lib/api";
import { cn } from "@/lib/cn";
import type { Product } from "@/lib/types";

/** Scan/enter a barcode (product or alternate-unit) and jump to the product. Scanners typically send Enter. */
export function BarcodeLookup() {
  const router = useRouter();
  const [code, setCode] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const lookup = async (e: React.FormEvent) => {
    e.preventDefault();
    const value = code.trim();
    if (!value) {
      setMessage("Enter or scan a barcode.");
      return;
    }
    setBusy(true);
    setMessage(null);
    try {
      const product = await api.get<Product>(`/api/products/by-barcode/${encodeURIComponent(value)}`);
      router.push(`/products/${product.id}`);
    } catch (err) {
      setMessage(err instanceof ApiError && err.status === 404 ? `No product with barcode “${value}”.` : err instanceof Error ? err.message : "Lookup failed.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={lookup} noValidate className="flex flex-col gap-1" role="search" aria-label="Barcode lookup">
      <div className="flex items-center gap-2">
        <div className="relative">
          <ScanBarcode className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-slate-400" aria-hidden />
          <input
            type="text"
            aria-label="Barcode"
            aria-invalid={message ? true : undefined}
            aria-describedby={message ? "barcode-lookup-msg" : undefined}
            placeholder="Scan or enter barcode"
            value={code}
            onChange={(e) => {
              setCode(e.target.value);
              if (message) setMessage(null);
            }}
            className={cn(controlClasses, "h-9 w-56 pl-9 font-mono")}
            autoComplete="off"
            spellCheck={false}
          />
        </div>
        <Button type="submit" variant="secondary" loading={busy}>
          Find
        </Button>
      </div>
      {message && (
        <p id="barcode-lookup-msg" role="status" className="text-xs font-medium text-red-600">
          {message}
        </p>
      )}
    </form>
  );
}
