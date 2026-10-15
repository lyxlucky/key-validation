# Account Security and Rebinding Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development. Execute through integration, review and verification without another approval checkpoint.

**Status:** Complete 2026-10-06; 133 backend tests, 17 browser tests, production build and migration check passed.

**Goal:** Deliver roadmap F01, F04, F12 under `docs/superpowers/specs/2026-10-05-account-security-rebinding-design.md`.
**Architecture:** Dedicated account-security service, shared rebinding rules, partial EF configuration, new React security page and license drawer panel.
**Tech stack:** Existing .NET 10/Identity/EF Core/PostgreSQL, React 19/Ant Design 6. No new runtime dependencies.

## Global constraints

Existing accounts remain usable; existing licenses remain unlimited for rebinding. New accounts/reset require password change; new licenses use 3 changes/30 days/24-hour cooldown. MFA is opt-in; five-minute challenge and sensitive-operation verification. Never log or store plaintext secrets in browser storage. Preserve CSRF, role enforcement, operation receipts, issuance recovery and lock order. No Git repository, so no commits/worktree deletion; review actual changed files and preserve evidence in docs.

### Task 1 — Account security backend

Own API account/auth files, Application UserDto, new Infrastructure AccountSecurityModel.cs, new auth HTTP tests. Root owns existing AppDbContext partial hooks and combined migration.

- [x] Add failing HTTP/algorithm tests for password changes, restricted first login, MFA setup/login/replay/recovery/step-up.
- [x] Implement the spec's API with encrypted secrets, serialized state changes, safe audits and stamp invalidation.
- [x] Adapt account lifecycle tests to the explicitly changed first-login contract without weakening authorization checks.
- [x] Run targeted tests after shared migration; report modified files, commands and output for independent review.

### Task 2 — Rebinding backend

Own Domain license/rebinding entities, Application licensing/rebinding files and ILicensingData, new API RebindingEndpoints, Infrastructure RebindingModel.cs, new tests. Do not edit UserDto or auth files.

- [x] Add meaningful failing tests for release limits, cooldown/window boundaries, concurrency, client proof and reset.
- [x] Implement common release policy, per-license configuration, history and reset, preserving old-data behavior.
- [x] Add endpoints under exact contract; keep application/license/binding/session lock order.
- [x] Run targeted tests after shared migration and report evidence for review.

### Task 3 — React security and rebinding

Own frontend src only. Backend workers supply exact contract above. Root owns E2E additions and generated schema.

- [x] Add security center, mandatory password change routing, TOTP enrollment QR/code, recovery codes, login challenge and step-up UX.
- [x] Add license device policy panel, counters, editing/reset reasons and query refresh.
- [x] Preserve pending issuance/operation state and login memory isolation; clear secrets when dialogs close.
- [x] Build/typecheck and provide frontend review report.

### Task 4 — Integration and delivery

Root owns existing AppDbContext hooks, shared migration, generated schema, browser tests, docs and coordinated publish/restart.

- [x] Generate/apply one new migration after both model workers finish.
- [x] Run full backend tests, publish local API, regenerate types and build frontend.
- [x] Extend browser workflows and viewport checks; adapt old account tests for first login.
- [x] Independent feature reviews and final integration review; fix concrete findings and rerun relevant tests.
- [x] Update roadmap status (3 delivered / 19 remaining), API contract, verification, plan and ledger accurately.
