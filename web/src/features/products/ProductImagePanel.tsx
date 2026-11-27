"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ImageOff, ImagePlus, Trash2, Upload } from "lucide-react";
import { useRef, useState } from "react";
import { Button } from "@/components/ui/Button";
import { Card, CardHeader } from "@/components/ui/Card";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { Skeleton } from "@/components/ui/Skeleton";
import { useToast } from "@/components/ui/Toast";
import { api, errorMessage } from "@/lib/api";
import { useObjectUrl } from "@/lib/hooks";
import { qk } from "@/lib/queries";
import type { Product } from "@/lib/types";

const MAX_BYTES = 2 * 1024 * 1024;
const ACCEPTED = ["image/png", "image/jpeg", "image/webp"];

/**
 * Kept outside the shared ["products"] root on purpose: realtime inventory events invalidate that root,
 * and re-downloading the image blob on every stock change would be wasteful.
 */
export const productImageKey = (id: string, version: number) => ["product-image", id, version] as const;

/** Product image fetched with the bearer token (an <img> cannot send it) and shown via an object URL. */
export function ProductImagePanel({ product, canManage }: { product: Product; canManage: boolean }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const inputRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmRemove, setConfirmRemove] = useState(false);

  const image = useQuery({
    queryKey: productImageKey(product.id, product.version),
    queryFn: ({ signal }) => api.blob(`/api/products/${product.id}/image`, signal),
    enabled: product.hasImage,
    staleTime: 5 * 60_000,
    gcTime: 5 * 60_000,
  });
  const url = useObjectUrl(product.hasImage ? image.data : null);

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: qk.product(product.id) });
    void queryClient.invalidateQueries({ queryKey: ["products", "list"] });
    void queryClient.invalidateQueries({ queryKey: ["product-image", product.id] });
  };

  const onFile = async (file: File | undefined) => {
    if (!file) return;
    setError(null);
    if (!ACCEPTED.includes(file.type)) {
      setError("Only PNG, JPEG and WebP images are supported.");
      return;
    }
    if (file.size === 0 || file.size > MAX_BYTES) {
      setError("Images must be smaller than 2 MB.");
      return;
    }
    setUploading(true);
    try {
      await api.upload(`/api/products/${product.id}/image`, file, "file", "PUT");
      toast.success("Image uploaded");
      await refresh();
    } catch (e) {
      setError(errorMessage(e));
      toast.error(e);
    } finally {
      setUploading(false);
      if (inputRef.current) inputRef.current.value = "";
    }
  };

  return (
    <Card>
      <CardHeader
        title="Image"
        actions={
          canManage ? (
            <>
              <input
                ref={inputRef}
                type="file"
                accept={ACCEPTED.join(",")}
                className="sr-only"
                tabIndex={-1}
                aria-hidden="true"
                onChange={(e) => void onFile(e.target.files?.[0])}
              />
              <Button
                size="sm"
                variant="secondary"
                loading={uploading}
                icon={product.hasImage ? <Upload className="size-3.5" /> : <ImagePlus className="size-3.5" />}
                onClick={() => inputRef.current?.click()}
              >
                {product.hasImage ? "Replace" : "Upload"}
              </Button>
              {product.hasImage && (
                <Button size="icon-sm" variant="danger-ghost" aria-label="Remove image" onClick={() => setConfirmRemove(true)}>
                  <Trash2 className="size-4" />
                </Button>
              )}
            </>
          ) : undefined
        }
      />
      <div className="p-4">
        {!product.hasImage ? (
          <div className="flex aspect-[4/3] flex-col items-center justify-center gap-2 rounded-md border border-dashed border-slate-300 bg-slate-50 text-slate-400">
            <ImageOff className="size-7" aria-hidden />
            <p className="text-xs">No image{canManage ? " — PNG, JPEG or WebP up to 2 MB" : ""}</p>
          </div>
        ) : image.isPending || (!url && !image.isError) ? (
          <Skeleton className="aspect-[4/3] w-full" />
        ) : image.isError ? (
          <div className="flex aspect-[4/3] flex-col items-center justify-center gap-2 rounded-md border border-slate-200 bg-slate-50 text-center text-xs text-slate-500">
            <ImageOff className="size-6 text-slate-400" aria-hidden />
            Could not load the image.
            <button type="button" className="font-medium text-brand-700 hover:underline" onClick={() => void image.refetch()}>
              Try again
            </button>
          </div>
        ) : (
          // eslint-disable-next-line @next/next/no-img-element -- blob: URL from an authenticated fetch
          <img src={url ?? undefined} alt={product.name} className="aspect-[4/3] w-full rounded-md border border-slate-200 bg-white object-contain" />
        )}
        {error && (
          <p role="alert" className="mt-2 text-xs font-medium text-red-600">
            {error}
          </p>
        )}
      </div>

      <ConfirmDialog
        open={confirmRemove}
        onClose={() => setConfirmRemove(false)}
        title="Remove product image?"
        description="The image will be deleted permanently."
        confirmLabel="Remove image"
        tone="danger"
        onConfirm={async () => {
          await api.delete(`/api/products/${product.id}/image`);
          toast.success("Image removed");
          await refresh();
        }}
      />
    </Card>
  );
}
