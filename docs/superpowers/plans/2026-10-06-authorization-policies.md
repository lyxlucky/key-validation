# Authorization Policies Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Track steps with checkboxes.

**Goal:** Deliver roadmap F02/F05/F06/F07/F08 with compatible data migration, management UI and online client enforcement.

**Architecture:** Extend the existing License aggregate and LicensingService partials. Isolate freeze history, trial policy and trial claims from expiring audit logs; preserve application/license row-lock transaction boundaries. Generate frontend contracts from the running API.

**Tech Stack:** ASP.NET Core 10, PostgreSQL 17, React / TypeScript, Ant Design, Playwright.

**Spec:** `docs/superpowers/specs/2026-10-06-authorization-policies-design.md`.

## Global Constraints

- User confirmed only these five features and the recommended rules on 2026-10-06.
- Preserve CSRF, account/MFA gates, HMAC/token/device digest storage and existing old-client payload compatibility.
- Workspace has no Git repository. Work in place, keep reports; no commits or worktree creation possible.
- No new runtime dependencies required. Every functional deliverable includes behavior tests; migration compatibility uses actual PostgreSQL.

### Task 1: Authorization policy backend

**Files:** Domain `License.cs`, `Records.cs`, new `AuthorizationPolicies.cs`; Application `Contracts.cs`, `ILicensingData.cs`, `LicensingService.cs`, `LicensingQueries.cs`, new `PolicyContracts.cs`, `LicensingPolicies.cs`, `LicensingTrials.cs`; Infrastructure `AppDbContext.cs`, new `AuthorizationPolicyModel.cs`, generated migration/snapshot; API new `PolicyEndpoints.cs`, `Program.cs`, `ActivationAdmission.cs`, `SecurityDocumentTransformer.cs`; new domain/integration tests.

**Interfaces:** Produces every endpoint and DTO in the spec, plus additive plan fields and activation/heartbeat fields. Trial activation shares ActivationDto. Formal activation handles upgrade automatically. `License.BatchId` and DTO BatchId become nullable; regular issuance remains unchanged.

- [x] Write failing domain/HTTP tests before behavior. First useful HTTP fixture: create a plan with three devices and one concurrent session; issue; activate device A (200); activate B (`SESSION_LIMIT_REACHED`); logout A; activate B (200); assert two bindings and one live session.

```csharp
var response = await admin.PostAsJsonAsync($"/api/admin/licenses/{id}/freeze", new { reason = "售后暂停" });
Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
var denied = await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, key, "device"));
Assert.Equal("LICENSE_FROZEN", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
```

- [x] Run targeted tests and capture missing behavior. Extend real fixtures for clock boundary, simultaneous activations and formal-upgrade failure rollback.
- [x] Implement new policy fields and immutable snapshots; factor common activation under the established ordered locks. Maintain freeze history and unique trial claims; reject invalid/unauthorized writes before mutation.

```csharp
// Lifetime advancement during freeze must not consume the paused interval.
ExpiresAt = now + (ExpiresAt!.Value - FrozenAt!.Value);
FrozenAt = null;
// Active-session counting excludes the same binding replaced by this request.
// Trial + formal license locks are acquired together in sorted ID order.
```

- [x] Generate/apply the migration, backfilling online limits from MaxDevices. Add a migration regression from the previous release and run all backend tests.
- [x] Report exact commands, test counts, ownership, edge cases and any concerns in `.superpowers/sdd/2026-10-06-authorization-policies/task-1-report.md`.

### Task 2: React management flows

**Files:** `frontend/src/features/applications/PlansDrawer.tsx`, new `TrialPolicyPanel.tsx`; `frontend/src/features/licenses/LicenseDrawer.tsx`, new `FreezePolicyPanel.tsx`, `LicensesPage.tsx`; `frontend/src/components/common.tsx`, `frontend/src/lib/api.ts`, generated `schema.d.ts`, types, feature CSS as needed; `frontend/e2e/policies.spec.ts`.

**Interfaces:** Consumes spec endpoints and generated DTO fields. All new UI uses existing Chinese components, permission flags, error handling and query invalidation.

- [x] Add browser behavior tests which exercise real API results and first observe missing controls.

```typescript
await page.getByRole('button', { name: '冻结授权', exact: true }).click();
await page.getByLabel('操作原因').fill('客户暂时停用');
await page.getByRole('button', { name: '确认冻结', exact: true }).click();
await expect(page.getByText('已冻结', { exact: true }).first()).toBeVisible();
```

- [x] Add plan fields for online count, optional activation days and feature tags; show snapshots in license details.
- [x] Add freeze/unfreeze reason form, history, disabled-state compatibility and renewal disable while frozen; add frozen / activation_expired filters and status labels.
- [x] Add application trial policy form (readable to all roles; writable only Admin), include disabled/default policy and feature labels.
- [x] Generate types from updated OpenAPI, build, run browser flows and inspect 375/768/1024/1440px layout. Record failures and fixes with evidence.

### Task 3: Client integration, review and delivery

**Files:** `samples/KeyValidation.Client/LicenseApi.cs`, `Program.cs`, new `LicenseCapabilities.cs`; new client tests; `README.md`, `docs/api-contract.md`, `docs/verification.md`, roadmap and execution ledger.

**Interfaces:** Add optional grant metadata without breaking old deserialization. Add `ActivateTrialAsync` with the public trial request; purchased activation remains the upgrade mechanism. Capabilities require a valid LeaseTracker and a feature in the latest grant.

- [x] First verify a regression test fails if expired leases still authorize features or missing features grant access.

```csharp
Assert.False(capabilities.Allows("export"));
// After a valid lease and a grant containing export, Allows(export) becomes true;
// after the manual clock passes the lease, it becomes false without a network call.
```

- [x] Add trial mode and optional required feature to the sample, retain existing activation mode, never print secrets.
- [x] Run client tests, full backend tests, frontend build, full E2E and EF pending-model check; independently review backend concurrency and the completed integration, repair concrete findings.
- [x] Update docs with actual verified results and mark only F02/F05/F06/F07/F08 complete; the other 14 roadmap items remain pending.

## Plan self-review

The backend owns shared API/model files. The controller owns UI and sample files, so implementation does not conflict. Nullable trial BatchId is covered by generated client types and UI display. Trial feature changes snapshot on initial claim, paid upgrade snapshots are already on the purchased license. Frozen/activation-expired filters, dashboard and expiry queries are included, avoiding a state transition invisible to operators.

