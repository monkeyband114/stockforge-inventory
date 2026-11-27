# StockForge Web

Next.js 16 (App Router, TypeScript strict, Tailwind CSS 4) frontend for the StockForge multi-tenant inventory API.

## Quick start

```bash
cp .env.example .env.local        # NEXT_PUBLIC_API_URL=http://localhost:8080
npm install
npm run dev                       # http://localhost:3000
```

The API must allow the web origin via CORS with credentials (`http://localhost:3000` by default).

Demo accounts (password `StockForge!2026`): `admin@acme.test`, `inventory@acme.test` (also a Globex member: try the org switcher), `whmanager@acme.test`, `staff@acme.test`, `finance@acme.test`, `auditor@acme.test`, `admin@globex.test`. Platform super admin: `superadmin@stockforge.local` / `SuperAdmin!2026`.

## Scripts

| Script | Purpose |
| --- | --- |
| `npm run dev` | Dev server on port 3000 |
| `npm run build` | Production build (`output: "standalone"`) |
| `npm start` | Serve the production build on port 3000 |
| `npm run typecheck` | `tsc --noEmit` |

## Docker

```bash
docker build --build-arg NEXT_PUBLIC_API_URL=https://api.example.com -t stockforge-web .
docker run -p 3000:3000 stockforge-web
```

`NEXT_PUBLIC_API_URL` is compiled into the browser bundle, so set it at build time. The runtime image runs the standalone server as a non-root user on port 3000.

## Architecture

- `src/lib/api.ts`: fetch wrapper. It handles the bearer token, a single-flight refresh on 401 (serialised across tabs with Web Locks), RFC 9457 problem parsing into `ApiError`, `Idempotency-Key` submissions, multipart upload and blob download.
- `src/lib/auth.tsx`: `AuthProvider` / `useAuth`. The access token is kept **in memory only**. The refresh token is the HttpOnly `sf_refresh` cookie. The session is restored on load via `POST /api/auth/refresh` and refreshed about 60 s before expiry. Sign-out is broadcast to other tabs.
- `src/lib/realtime.tsx`: SignalR connection to `/hubs/notifications`. Events invalidate TanStack Query caches. User-facing events go to the notification centre (the bell).
- `src/lib/queries.ts`: query keys and shared queries (organization, warehouses, categories, units, products).
- `src/components/ui`: accessible primitives (dialogs and drawers trap focus and close on Esc).
- `src/components/layout`: app shell, sidebar and top bar.
- `src/features/*`: feature components.
- `src/app/(public)`: sign-in, registration, password reset and invitation pages.
- `src/app/(app)`: authenticated pages.

Navigation items and actions are hidden when the user lacks the permission. The API still enforces every permission.

### Browser note

The refresh cookie is `Secure; SameSite=Strict`. Over plain HTTP it is only stored by browsers that treat `localhost` as a secure context (Chrome, Edge, Firefox). In any other deployment, serve the API and web over HTTPS on the same site.
