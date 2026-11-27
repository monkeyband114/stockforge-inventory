"use client";

import { useQueryClient } from "@tanstack/react-query";
import { Clock, Download, RefreshCw } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { useToast } from "@/components/ui/Toast";
import { api, ApiError } from "@/lib/api";

function saveBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1_000);
}

/**
 * Downloads the invoice PDF with the bearer token (an <a href> cannot send it). While the worker is still
 * rendering, the API answers 404 `pdf_pending`: show a "being generated" state with a retry instead of an error.
 */
export function InvoicePdfButton({ invoiceId, number, hasPdf, size = "md" }: { invoiceId: string; number: string; hasPdf: boolean; size?: "sm" | "md" }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [pending, setPending] = useState(!hasPdf);

  const download = async () => {
    setBusy(true);
    try {
      const blob = await api.blob(`/api/invoices/${invoiceId}/pdf`);
      saveBlob(blob, `${number}.pdf`);
      setPending(false);
    } catch (e) {
      if (e instanceof ApiError && e.code === "pdf_pending") {
        setPending(true);
        toast.info("PDF is still being generated", "It is rendered in the background. Try again in a moment.");
      } else {
        toast.error(e);
      }
    } finally {
      setBusy(false);
    }
  };

  const checkAgain = async () => {
    setBusy(true);
    await queryClient.invalidateQueries({ queryKey: ["invoices"] });
    await queryClient.invalidateQueries({ queryKey: ["orders"] });
    setBusy(false);
    await download();
  };

  if (pending) {
    return (
      <div className="flex flex-wrap items-center gap-2">
        <span className="inline-flex items-center gap-1.5 text-xs text-slate-500" role="status">
          <Clock className="size-3.5" aria-hidden /> PDF is being generated
        </span>
        <Button variant="secondary" size="sm" icon={<RefreshCw className="size-3.5" />} loading={busy} onClick={checkAgain}>
          Check again
        </Button>
      </div>
    );
  }

  return (
    <Button variant="secondary" size={size} icon={<Download className="size-4" />} loading={busy} onClick={download}>
      Download PDF
    </Button>
  );
}
