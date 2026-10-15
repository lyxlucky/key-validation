# Key Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Deliver a runnable C# + React software licensing system with secure issuance, device binding, expiring sessions, administrative controls, and tests.

**Architecture:** Separately built React SPA and modular ASP.NET Core API share an explicit HTTP contract. PostgreSQL transactions serialize mutations of a license; ASP.NET Core Identity authenticates administrators with cookies and CSRF protection. Client sessions use opaque random bearer tokens.

**Tech Stack:** .NET 10, EF Core 10/Npgsql, PostgreSQL 17, React, TypeScript, Vite, TanStack Query, Ant Design, xUnit, Playwright.

**Spec:** `docs/superpowers/specs/2026-10-05-key-validation-design.md` (user approved 2026-10-05).

## Global Constraints

- Chinese administration UI; independent frontend/backend builds; API routes start `/api/admin` or `/api/client`.
- UTC expiry on first activation; device limits 1–100; default one device; 60-second heartbeat; 180-second lease; 8-hour absolute session lifetime.
- At least 160 random bits per license; HMAC-SHA-256 storage only; versioned secret keys. Session tokens have at least 256 random bits and hash-only storage.
- Batch maximum 1,000; page default 20, maximum 100; issuance is idempotent by actor and request ID and returns plaintext once only.
- Admin login uses Identity, Secure/HttpOnly cookies in production, 30-minute idle timeout, 8-hour absolute limit and CSRF validation. No public registration or default password.
- Disable and unbind revoke existing sessions; restoring does not restore sessions or extend expiry.
- Real PostgreSQL concurrency tests are required. No substitution of in-memory tests for row-lock behavior.
- No existing Git repository is available; work directly in the supplied workspace and record files/test evidence without inventing commits.
- Package caches, tools and generated local credentials used during verification belong in ignored workspace directories. Never print secrets.

## Task 1: Backend licensing and administration

**Files:** create `backend/KeyValidation.slnx`, project files under `backend/src/KeyValidation.{Api,Application,Domain,Infrastructure}/`, tests under `backend/tests/KeyValidation.{Tests,IntegrationTests}/`, `backend/src/KeyValidation.Infrastructure/Migrations/`, `docs/api-contract.md`.

**Interfaces:** consumes environment configuration `ConnectionStrings__Database`, `Licensing__CurrentKeyVersion`, `Licensing__Keys__v1`, optional `DataProtection__Path`. Produces versioned `/api` routes from the spec, complete OpenAPI at `/openapi/v1.json`, and CLI operations `migrate`, `admin create`, `admin reset-password` (password via environment or hidden prompt). Exact JSON payloads are documented in `docs/api-contract.md` for Task 2.

- [x] Create project references and dependency configuration; introduce testable license and session state boundaries.
- [x] Write focused failing xUnit tests that catch repeated activation extending expiry, expiry boundary errors, device capacity overflow, reviving revoked sessions, and incorrect secret normalization. Example expected behavior:

```csharp
var now = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
var license = License.Issue(applicationId, batchId, digest, "v1", "K1-…ABCD", 86400, 1);
license.Activate(now);
license.Activate(now.AddHours(1));
Assert.Equal(now.AddDays(1), license.ExpiresAt);
```

The implementer may choose domain method signatures, but assertions must express these observable outcomes. Run `dotnet test backend/KeyValidation.slnx` and capture red evidence before implementing each tested behavior.
- [x] Implement the domain rules, key generation/digests, EF mappings/migrations, transactional application services and administrative use cases. Transactions acquire application, license, binding and session locks in consistent order; bulk operations sort IDs.
- [x] Add Identity cookies, CSRF, role policies, Problem Details, rate limits, explicit development CORS, no-store secret responses, bounded inputs, health/readiness and safe structured logging. Never expose entities or secret digests directly through API serialization.
- [x] Implement the complete endpoint table from the spec, plus CLI account management, audit/event retention and deployment migration command.
- [x] Run real PostgreSQL integration tests for two-device activation races, concurrent duplicate issuance, disable/heartbeat behavior, login/CSRF/role restrictions, and response-loss recovery. Integration fixtures must fail clearly when the requested PostgreSQL server is unavailable; they must not silently pass without executing.
- [x] Generate and inspect the initial migration, run tests and build, document precise API payloads and commands. Keep a report containing actual red/green commands and results.

## Task 2: React administration application

**Files:** create `frontend/package.json`, `frontend/vite.config.ts`, `frontend/src/main.tsx`, `frontend/src/App.tsx`, `frontend/src/lib/api.ts`, `frontend/src/lib/schema.d.ts`, `frontend/src/features/{auth,dashboard,applications,licenses,audit}/`, `frontend/src/styles.css`, `frontend/e2e/admin.spec.ts`, `frontend/playwright.config.ts`.

**Interfaces:** consumes Task 1 OpenAPI and `docs/api-contract.md`; Vite forwards `/api` to `http://127.0.0.1:5080`. Produces a static `frontend/dist` and accessible Chinese UI. API client uses credentials, retrieves a CSRF token, refreshes it after login, translates Problem Details into useful feedback, and returns to login on 401.

- [x] Initialize React/TypeScript/Vite with Router, Query and Ant Design, scripts `dev`, `build`, `test:e2e`, `api:types`.
- [x] Write a Playwright test against the real API for login → create application → create plan → generate batch → save one-time keys → filter licenses → disable → audit. Before implementing pages, run it and record the expected missing-UI failure.

```typescript
await page.goto('/');
await page.getByLabel('用户名').fill(process.env.E2E_ADMIN_USER!);
await page.getByLabel('密码').fill(process.env.E2E_ADMIN_PASSWORD!);
await page.getByRole('button', { name: '登录', exact: true }).click();
await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
```

- [x] Implement a cohesive responsive sidebar shell, session initialization, loading/error/empty states, permission-aware navigation and actions. Do not include fake production data.
- [x] Implement dashboard, application and plan administration, batch generation with an explicit one-time download, license pagination/search/status filtering and details including sessions/bindings, disable/restore/release/revoke confirmations, and audit filtering.
- [x] Keep plaintext issued keys only in transient component memory; disable automatic retries for batch issuance; preserve the request ID during error recovery and provide an actionable link to the committed batch when duplicate response is returned.
- [x] Generate TypeScript types from actual OpenAPI, build production assets, run E2E and test API failure/authorization behavior.

## Task 3: Packaging, integrated verification and handoff

**Files:** create `README.md`, `.gitignore`, `.env.example`, `deploy/compose.yaml`, `deploy/nginx.conf`, `deploy/backend.Dockerfile`, `deploy/frontend.Dockerfile`, `scripts/start-dev.ps1`, `samples/KeyValidation.Client/`, `docs/verification.md`.

**Interfaces:** consumes build outputs and CLI commands from Task 1, assets from Task 2. Produces documented local and container deployment plus a C# client that performs activation/heartbeat/logout and stops on lease expiry.

- [x] Provide local PostgreSQL setup for verification, isolated database names, hidden background processes and cleanup limited to this workspace's process IDs.
- [x] Provide Docker build stages and compose services; persistent database and Data Protection keys, reverse proxy `/api`, health checks and explicit migration command. Secrets are supplied externally; production configuration refuses missing/insecure required secrets.
- [x] Provide a C# console client example reading license and device configuration from environment; never print the card or session token. Track leases with a monotonic clock and enforce the earlier of current lease and absolute expiry.
- [x] Document prerequisites, environment variables, account setup, development commands, tests, production TLS/proxy assumptions, migrations/backup and unsupported offline licensing.
- [x] Run `dotnet build backend/KeyValidation.slnx`, complete xUnit suites against PostgreSQL, `npm run build`, Playwright real-browser E2E, and a sample-client smoke test against the real API.
- [x] Obtain independent security/concurrency/spec review, fix material findings, run affected verification and record evidence in `docs/verification.md`. Report unexecuted checks explicitly.

## Execution record

Progress, rulings, review reports and test outputs are recorded in `.superpowers/sdd/2026-10-05-key-validation/`. The user has approved implementation; routine task transitions do not require another confirmation.

