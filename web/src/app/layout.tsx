import type { Metadata, Viewport } from "next";
import type { ReactNode } from "react";
import "./globals.css";
import { Providers } from "./providers";

export const metadata: Metadata = {
  title: { default: "StockForge", template: "%s · StockForge" },
  description: "Multi-tenant inventory management",
  robots: { index: false, follow: false },
};

export const viewport: Viewport = {
  themeColor: "#0B1B3A",
  width: "device-width",
  initialScale: 1,
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body>
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
