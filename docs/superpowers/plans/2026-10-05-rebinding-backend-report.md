# F04 rebinding backend implementation report

Scope: Task 2 of `2026-10-05-account-security-rebinding.md`, under the matching binding design spec. No new packages or Git operations.

## Implementation

- `backend/src/KeyValidation.Domain/License.cs`: new issuance enables default rebinding policy; session validity checks extracted so release can validate without renewing a lease.
- `backend/src/KeyValidation.Domain/DeviceRebinding.cs`: per-license policy, reset generation, quota/cooldown evaluation, and independent release events.
- `backend/src/KeyValidation.Application/ILicensingData.cs`: exposes independent release history.
- `backend/src/KeyValidation.Application/RebindingContracts.cs`: exact policy/update/reset HTTP DTOs; required fields use existing JSON validation conventions.
- `backend/src/KeyValidation.Application/LicensingRebinding.cs`: coherent policy reads, reasoned configuration/reset, current-session release, common release implementation.
- `backend/src/KeyValidation.Application/LicensingService.cs`: existing administrator release delegates to the common rules and emits no duplicate release audit.
- `backend/src/KeyValidation.Infrastructure/RebindingModel.cs`: partial EF model and DbSet; old-data policy disabled by database default; indexed release history with one event per binding.
- `backend/src/KeyValidation.Api/RebindingEndpoints.cs`: policy GET/PATCH/reset and current-device POST release; parent integrates route mapping and combined migration.
- `backend/tests/KeyValidation.Tests/RebindingRulesTests.cs`: domain boundaries, reduced quota, combined blockers, disabled policy, issuance and reset invariants.
- `backend/tests/KeyValidation.IntegrationTests/RebindingTests.cs`: HTTP policy, release, same-time reset, authorization/CSRF, malformed policy, concurrency, token validity, retention and OpenAPI coverage.
- `backend/tests/KeyValidation.IntegrationTests/RebindingMigrationTests.cs`: real previous-schema-to-new-schema regression using the test's isolated PostgreSQL database.

## Decisions and self-review

- Administrator duplicate release returns 204 before policy evaluation, without another event or audit. A client's second call with the revoked release token is rejected: the spec explicitly requires a live session and rejects revoked tokens. Client requests cannot choose a binding; tests supply another real live device's ID and verify it stays bound.
- Application → License → Binding → Session locks are acquired through the existing helpers for release, policy read, configuration and reset. No history is queried before the license lock for a mutation.
- Only `(now - window, now]` timestamps count; cooldown expires inclusively at its endpoint. `nextAllowedAt` is the later of quota recovery and cooldown, null when immediately allowed or policy disabled. If quota and cooldown both block, the stable error is `REBIND_LIMIT_REACHED`.
- Reducing a quota below current usage waits until enough old events leave the window. Disabled policies still create events. Reset advances a generation rather than using a timestamp boundary, retains all events, and changes no license expiry, binding or session.
- Database defaults leave migrated licenses unrestricted; `License.Issue` explicitly enables 3/30/24 for new licenses. History is independent of `AuditLogs` and untouched by retention cleanup.
- Reads query bounded-window timestamps plus the last current-generation event, avoiding loading the complete history. Policy writes and releases commit history/audit/state in one transaction.
- OpenAPI review found the pre-existing client security transformer only matched `/client/sessions`; account worker added the new release route. The passing HTTP test checks its bearer scheme and lack of request body.

## Verification

Initial red command, using existing local NuGet cache and real PostgreSQL:

```powershell
$env:NUGET_PACKAGES = 'D:\VSCODEWORKSPACE\key-validation\.local\nuget-packages'
dotnet test backend/tests/KeyValidation.IntegrationTests --no-restore --filter FullyQualifiedName~RebindingTests --logger 'console;verbosity=minimal'
```

Result before production implementation: compilation succeeded; 2 tests failed as expected because policy and client release returned 404 instead of 200/401. No test skipped.

After the combined migration and coordinated integration build:

```powershell
dotnet test backend/tests/KeyValidation.IntegrationTests --no-build --no-restore --filter FullyQualifiedName~Rebinding --logger 'console;verbosity=minimal'
```

Result: **23 passed, 0 failed, 0 skipped**, including the real migration downgrade/forward compatibility test. Duration reported: 9 seconds. This runs against real local PostgreSQL with isolated per-fixture databases; no connection values or secrets were printed.

```powershell
dotnet test backend/tests/KeyValidation.Tests --no-restore --filter FullyQualifiedName~RebindingRulesTests --logger 'console;verbosity=minimal'
```

Result: **6 passed, 0 failed, 0 skipped**; domain/application/infrastructure/unit-test compilation completed without warnings or errors. The first `--no-build` unit attempt found no matching tests in its old DLL, so the recorded passing run above rebuilt the unit project.

Parent owns the full cross-feature baseline, local publish and browser verification.

## Independent reviews

The account-security worker independently reviewed the rebinding service/domain/model/routes/shared locks/migration/tests and reported no blocking findings. They checked lock-before-history ordering, generation reset at identical timestamps, reduced quota recovery time, client proof reloaded under locks, legacy defaults and independent retention.

I independently reviewed the account-security backend against the spec, including `AccountSecurity.cs`, `AccountSecurityEndpoints.cs`, `Totp.cs`, `Program.cs`, `AdminAccounts.cs`, `AdminCli.cs`, the partial model and HTTP tests. No documented coding-standard violations were found. One concrete spec finding: CLI creation initially exempted Operator/ReadOnly as well as the bootstrap administrator from mandatory first password change. The account worker accepted this finding and is adding a regression and fix; administrator bootstrap remains usable. Their separately identified queued stale-cookie account-write issue was already corrected and passed targeted tests when reviewed. No other blocking findings were identified in serialization, secret persistence, one-use factor consumption, challenge/stamp invalidation or step-up enforcement.
