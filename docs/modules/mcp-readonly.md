# Read-only MCP server for AI clients — 2026-09-26

Client (Uzbek, 2026-09-26): expose the whole system to AI assistants (ChatGPT, Claude, any standard
MCP client) **read-only**. Confirmed decisions:

1. **Who:** leadership only. Allowed: `superadmin`, `admin`, and `staff` whose access role carries
   the permission key `aiAccess` (a new checkbox in Boshqaruv → Rollar, superadmin-managed like every
   role). Teachers, cashiers, pupils, parents — never.
2. **How they connect:** a *standard* remote MCP server. When the user adds it in the AI client, the
   client opens **our** login page in the browser (OAuth 2.1 authorization-code + PKCE, per the MCP
   authorization spec) — the password is typed into our page, never into the AI.
3. **What:** all school data, read-only, but never more than the signed-in user may see in the web
   panel (their role / page permissions still apply; finance needs finance access, etc.).

## Architecture
- Inside the existing backend (`SchoolLms.Server`), not a separate service: same DB, same services,
  same RBAC. Streamable HTTP transport at **`/mcp`** using the official C# SDK
  (`ModelContextProtocol.AspNetCore`, pick the version compatible with .NET 10).
- **Read-only, defence in depth:**
  1. Only read tools exist — no tool writes, deletes, sends messages, exports-and-clears, or
     triggers jobs/side effects.
  2. MCP tools run on a separate `DbContext` using a **read-only PostgreSQL role** (`app_ro`, SELECT
     only, created/maintained in `deploy/init-roles.sql`, connection string
     `ConnectionStrings__ReadOnly`, password `DB_RO_PASSWORD` in `.env`). If the RO connection string
     is not configured the MCP endpoint is **off** (fail-closed), the rest of the app is unaffected.
- **Never exposed:** password hashes, initial passwords, logins/credentials, OAuth/Telegram/API
  tokens, bot tokens, integration secrets, `.env`-derived settings.

## OAuth 2.1 (MCP authorization spec)
- `/.well-known/oauth-protected-resource` (RFC 9728) → points to our authorization server.
- `/.well-known/oauth-authorization-server` (RFC 8414).
- `POST /oauth/register` — dynamic client registration (RFC 7591); store clients (name, redirect
  URIs). Validate redirect URIs (https, or loopback for desktop clients).
- `GET /oauth/authorize` — our login page (server-rendered, minimal, our brand: yellow #FFD006,
  Uzbek text): login + password → consent screen ("«<client name>» maktab ma'lumotlarini faqat
  o'qish uchun so'rayapti") → redirect with code. PKCE **S256 required**. Rejects users not allowed
  by rule 1 with a clear message. Rate-limited like `/api/auth/login`.
- `POST /oauth/token` — authorization_code (+PKCE verify) and refresh_token grants. Opaque tokens,
  stored **hashed**; access token ~1 h, refresh ~30 days with rotation.
- `/mcp` requires `Authorization: Bearer`; 401 includes `WWW-Authenticate` with
  `resource_metadata` so clients start the flow. On every request re-check: user exists, not
  blocked, still allowed (role/aiAccess), token not revoked — same freshness rule as the web panel.
- Revocation: Boshqaruv → "AI ulanishlar" (superadmin): list connected clients per user (client
  name, user, last used), revoke. Revoking a user's `aiAccess` or deleting the user kills tokens.

## Audit & limits
- `mcp_audit` table: user, client, tool, short argument summary, row count, timestamp. Visible on the
  "AI ulanishlar" page.
- Per-token rate limit; page size caps on every list tool (e.g. max 200 rows), date-range caps.

## Tools (read-only; each checks the user's permission for that area)
Reuse existing query services — do not duplicate business logic. Uzbek-friendly descriptions.
- School overview (counts: pupils, classes, tracks, teachers, today's attendance).
- Pupils: search (name/phone/class/track/status/debt filters); profile (class, track group,
  guardians, status, subscriptions, balance, attendance summary, grades summary, discipline points).
- Classes & track groups: list (with stats), roster, class performance.
- Timetable: by class/track, by teacher, by day.
- Attendance: daily overview, per class/lesson, analytics for a date range (excused/unexcused/late),
  boarding (evening/dorm).
- Journal/grades: marks by owner/subject/quarter; seasonal marks; block-test results.
- Finance (finance access): debtors, arrears pivot, transactions journal, invoices, payments,
  P&L / cash-flow summaries, salary report, discounts, expenses.
- Staff: employees list (teachers + staff, position, role); salary only with finance access.
- Leads, surveys/submissions, admission candidates & results.
- Discipline incidents & ratings; messages/broadcast **history** (read); certificates; contracts list.

## Tests (mandatory)
- OAuth: metadata docs; DCR; authorize with PKCE (success, wrong password, not-allowed user, bad
  redirect, missing/plain PKCE); token exchange; refresh rotation; revoked token → 401.
- MCP protocol: initialize, tools/list, tools/call for each tool family (happy path + permission
  denied for a user without that area).
- Read-only guarantee: the RO DB role cannot INSERT/UPDATE/DELETE (test against the role); no tool
  name/handler performs writes (reflection check: tools only call the RO context).
- Secrets never appear in any tool output (scan outputs for hash/password/token field names).
- `ViewAccessSweepTests` and the full suite stay green.

## Deploy notes
- New env: `DB_RO_PASSWORD`, `ConnectionStrings__ReadOnly`; `init-roles.sql` creates/grants `app_ro`.
- Caddy/Cloudflare: `/mcp`, `/oauth/*`, `/.well-known/*` reachable on the public domain.
- `docs/MCP.md`: how leadership connects from ChatGPT / Claude / other MCP clients.
