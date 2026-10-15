# Management Expansion Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development for isolated tasks, then integration and review. Track checkboxes.

**Goal:** Deliver six approved administration features and preserve the remaining 22 as a future spec.
**Status:** Complete, 2026-10-05. 77 backend tests and 15 browser tests passed; frontend production build and migration consistency verified. See `docs/verification.md` and the adjacent management-expansion-review record.
**Architecture:** Add account and license-operations modules to existing API, persist operation receipts for idempotency, extend React feature routes, retain core authorization protocol.
**Tech Stack:** .NET10, EF Core/Npgsql, React19/Ant Design6, Playwright.
**Spec:** `docs/superpowers/specs/2026-10-05-management-expansion-design.md`

## Global Constraints

Preserve existing API compatibility, CSRF, roles, locking order and one-time plaintext issuance. No Git repo; no commits. Renewal follows the explicitly accepted unused/active/expired rules. At most100 selected IDs. Reasons required for operations. No destructive data deletion. Keep existing UI tokens and responsive behavior.

### Task1: Licensing operations backend

Files: Domain License/Records; Application Contracts/ILicensingData/LicensingQueries/new LicensingOperations; API new ManagementEndpoints; new unit/integration tests.

- [x] Add failing domain renewal tests and integration cases for duplicate extension, partial results, role denial and expiry filters.
- [x] Implement renewal, metadata, receipt-backed operations, batch overview and expiry queries matching spec JSON.
- [x] Retain scope-specific receipt lookup, ordered locks, bounds and safe audit payloads.
- [x] Run domain and PostgreSQL integration tests after shared migration is available.

### Task2: Accounts backend

Files: API new AdminAccounts, Program.cs/Endpoints.cs only account auth changes; new account integration tests.

- [x] Add failing HTTP cases for lifecycle, role/cookie invalidation, self-protection and last Admin.
- [x] Implement Admin-only list/create/role/disable/enable/reset endpoints using Identity; use transaction/advisory lock for account mutations.
- [x] Require enabled accounts at sign-in and cookie validation; use security stamp invalidation and safe auditing.
- [x] Verify after shared migration.

### Task3: React management features

Files: frontend feature account/batch/expiry/operations pages; licenses views; App routes; lib types/errors; scoped styles.

- [x] Add account and batch pages, expiry list, license tags/notes and selection toolbar.
- [x] Implement single/bulk operation dialog with preview, reason, per-item result and scoped recovery state; snapshot request before network.
- [x] Keep pending payload immutable during ambiguous retry; query receipt to recover. Never persist passwords or plaintext.
- [x] Extend browser workflows and viewport checks centrally after API ready.

### Task4: Integration, migration and delivery

Files: Infrastructure AppDbContext/Migrations, generated schema, docs/api-contract, docs/verification.

- [x] Add shared model mappings and generate a new EF migration; apply against local dev and isolated test databases.
- [x] Register new endpoints, publish API, regenerate OpenAPI TypeScript schema and align frontend types.
- [x] Run backend tests, frontend build, original and new real API browser workflows, responsive checks.
- [x] Independent review all feature boundaries; fix substantive findings and retest affected flows.
- [x] Mark both specs accurately and document exact validation evidence.

## Self-review

Backend tasks share only an explicitly root-owned Infrastructure mapping/migration; account contracts remain local to Accounts module. Licensing operation types form the frontend contract. Accounts changes in existing Endpoints are restricted to login checks; root adds new map registrations. No worker writes another worker's files. All six features and the deferred spec have a mapped deliverable.
