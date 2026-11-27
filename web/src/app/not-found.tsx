import Link from "next/link";

export default function NotFound() {
  return (
    <main className="flex min-h-dvh flex-col items-center justify-center px-6 text-center">
      <p className="text-sm font-semibold text-brand-700">404</p>
      <h1 className="mt-2 text-2xl font-semibold text-slate-900">Page not found</h1>
      <p className="mt-2 text-sm text-slate-500">The page you are looking for does not exist or has moved.</p>
      <Link href="/" className="mt-6 rounded-md bg-brand-700 px-4 py-2 text-sm font-medium text-white hover:bg-brand-600">
        Back to StockForge
      </Link>
    </main>
  );
}
