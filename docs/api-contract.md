# Key Validation HTTP contract

API base URL `/api`. JSON uses camelCase; IDs are UUID strings, timestamps ISO-8601 UTC. Accounts use multiple assignable roles; the built-in roles are `Admin`, `Operator`, and `ReadOnly`. All paged list responses use `{ "items": [], "total": 0, "page": 1, "pageSize": 20 }`. List query parameters `page` (default 1), `pageSize` (default 20, maximum 100). Unknown resources return 404. Empty successful mutations return 204.

All failures are `application/problem+json`: `{ "type": "about:blank", "title": "LICENSE_INVALID", "status": 403, "code": "LICENSE_INVALID", "traceId": "..." }`. A duplicate batch returns 409 with `code: "BATCH_ALREADY_CREATED"` and `batchId`; changed parameters under the same actor/request ID return `IDEMPOTENCY_CONFLICT`. Validation is 400, unauthenticated 401, forbidden 403, concurrency/idempotency 409, rate limit 429 with `Retry-After`.

## Authentication

Use `credentials: 'include'` for admin requests. `GET /admin/auth/csrf` returns `{ "token": "..." }` and sets the CSRF cookie. Send the token in `X-CSRF-TOKEN` for **every admin mutation**, including POST/PATCH/PUT, login and license search. Refresh the CSRF token after login because it is bound to identity. Admin cookies are HttpOnly and Secure outside Development/Testing. Idle lifetime 30 minutes, absolute lifetime 8 hours. Never persist passwords, tokens, plaintext keys, or issuance results in browser storage.

| Method and path | Request | Response / authorization |
| --- | --- | --- |
| POST `/admin/auth/login` | `{ "userName": "admin", "password": "..." }` | 200 User or `{ "requiresTwoFactor": true, "challengeToken": "..." }`; anonymous with CSRF |
| POST `/admin/auth/two-factor` | `{ "challengeToken": "...", "code": "...", "useRecoveryCode": false }` | 200 User after completing the challenge |
| GET `/admin/auth/me` | — | 200 `{ "id": "uuid", "userName": "admin", "roles": ["Admin"], "mustChangePassword": false, "twoFactorEnabled": false }` |
| POST `/admin/auth/logout` | no body | 204 |
| GET `/admin/dashboard` | — | `{ "totalLicenses": 0, "activatedLicenses": 0, "validLicenses": 0, "onlineSessions": 0 }` |

Effective permissions are the union of an account's role grants, with default denial. Application and channel scope independently restrict data access. `platform.manage` permits account, role and platform configuration management; it does not imply business permissions. Licensing queries require `licenses.read`, issuance requires `licenses.issue`, and other licensing mutations require `licenses.manage`. Operation receipts are scoped to the current account and its current resource access.

An MFA challenge does not grant admin access or issue an application cookie. It lasts five minutes and is bound to the account, security stamp and active challenge ID; a new password login replaces that account's prior challenge. Refresh CSRF after receiving a challenge, after completing login, and after operations that reissue the current cookie. Challenge tokens remain in page memory only.

When `mustChangePassword` is true, authenticated sessions can only read auth me/CSRF/security status, change their password, or log out; the legacy `/admin/security/verify` endpoint also remains callable. MFA completed during login authorizes the password change without another code. Other resources return 403 `PASSWORD_CHANGE_REQUIRED`. Enforced restrictions apply on the server as well as the UI.

## Account security

All roles can manage their own account at `/admin/security`. SecurityStatus: `{ "mustChangePassword": false, "twoFactorEnabled": false, "recoveryCodesRemaining": 0, "verificationExpiresAt": null }`.

For an MFA-verified session, `verificationExpiresAt` is the current cookie expiry capped by the original eight-hour session deadline. It can move with sliding cookie renewal and does not schedule another code prompt. It is null when MFA is disabled.

| Method and path | Request | Response |
| --- | --- | --- |
| GET `/admin/security` | — | SecurityStatus |
| POST `/admin/security/password` | `{ "currentPassword": "...", "newPassword": "..." }` | User; current session reissued, other sessions invalidated |
| POST `/admin/security/two-factor/setup` | `{ "password": "..." }` | `{ "sharedKey": "...", "authenticatorUri": "otpauth://..." }`; pending setup lasts five minutes |
| POST `/admin/security/two-factor/confirm` | `{ "code": "123456" }` | `{ "recoveryCodes": ["..."], "user": User }`; enables MFA after successful code verification |
| POST `/admin/security/two-factor/disable` | `{ "password": "..." }` | User; requires an MFA-verified session and invalidates other sessions |
| POST `/admin/security/two-factor/recovery-codes` | `{ "password": "..." }` | `{ "recoveryCodes": ["..."] }`; requires an MFA-verified session and replaces every previous recovery code |
| POST `/admin/security/verify` | `{ "code": "...", "useRecoveryCode": false }` | 204; optional compatibility endpoint for already verified sessions; validates and consumes the code without extending session lifetime |

Password changes require the current password, existing Identity strength and a different new password. New API accounts, new CLI Operator/ReadOnly accounts, and administrator/CLI resets require a password change. Pre-migration accounts and CLI bootstrap Admin accounts retain existing access. Password resets do not disable MFA.

TOTP uses SHA1, six digits and 30-second steps, with one adjacent step in each direction allowed. A previously accepted or older step cannot be reused. Active and pending authenticator secrets are encrypted with Data Protection; persist the configured key directory across restarts. Ten 128-bit random recovery codes are returned once; only SHA256 digests are stored. Consumption is serialized so concurrent reuse cannot succeed. Enrollment, disable and regeneration never expose another account's secrets. The UI keeps codes and enrollment material only until their dialog closes.

For MFA-enabled accounts, completing TOTP or recovery-code login, or confirming enrollment, verifies the entire authenticated session. Later business writes and password changes do not require another code. Disabling MFA and regenerating recovery codes require the current password in that verified session; when MFA is disabled they return 409 `TWO_FACTOR_NOT_ENABLED`. The existing 30-minute idle and eight-hour absolute session limits still apply. Password changes preserve the current session's verification and original absolute deadline while invalidating other sessions. Missing, invalid, expired or revoked authentication requires a new login and returns 401 `UNAUTHENTICATED` before protected access or mutation; the compatibility verification endpoint cannot restore an incomplete session. Login and enrollment challenges still expire after five minutes.

Invalid code, current password or challenge returns 400 with `TWO_FACTOR_INVALID`, `CURRENT_PASSWORD_INVALID` or `TWO_FACTOR_CHALLENGE_INVALID`. Incorrect credentials and codes count toward the account lockout and authentication/security endpoints share the configured login IP limit.

## Account management

Effective access is returned by `GET /admin/auth/access` as `{permissions, allApplications, applicationIds, channelId}`. `platform.manage` can read and replace account application scope at `GET/PUT /admin/accounts/{id}/access`; new callers send `{allApplications, applicationIds}`. An empty application array denies application data when `allApplications=false`; the array is ignored when true. Channel membership is managed through `/admin/channels`, so do not submit a non-null `channelId` to the account scope endpoint. The channel's application set and the account's application scope intersect.

Permissions are `licenses.read`, `licenses.issue`, `licenses.manage`, `reports.read`, `audit.read`, `risk.review`, `customers.manage`, `orders.manage`, `integrations.manage`, and `platform.manage`. Built-in Admin has all ten. Initial Operator grants exclude `platform.manage` and `integrations.manage`; initial ReadOnly grants are the three read permissions. Role grants are editable except for the protected Admin role, and restarting does not reseed removed grants. Scope is applied before totals, paging and aggregation, including nested bindings, sessions, batches, audits and saved results. Missing permission is 403; an invisible resource is 404. Role-grant, membership and scope changes invalidate affected cookies. In-flight writes and cached replays recheck current grants and security stamps under the account-change lock before commit/return.

All `/admin/accounts` endpoints require `platform.manage`. Account: `{ "id": "uuid", "userName": "operator", "roles": ["Operator"], "roleIds": ["uuid"], "disabledAt": null, "createdAt": "...", "mustChangePassword": true, "twoFactorEnabled": false }`.

| Method and path | Request | Response |
| --- | --- | --- |
| GET `/admin/accounts` | pagination; optional `search` (username, at most 128 characters) | page of Account |
| POST `/admin/accounts` | `{ "userName": "operator", "password": "...", "roleIds": ["uuid"] }` | 201 Account |
| PATCH `/admin/accounts/{id}/roles` | `{ "roleIds": ["uuid", "uuid"] }` | 200 Account; replaces memberships |
| PATCH `/admin/accounts/{id}/role` | `{ "role": "ReadOnly" }` | 200 Account; legacy adapter |
| POST `/admin/accounts/{id}/disable` | no body | 204 |
| POST `/admin/accounts/{id}/enable` | no body | 204 |
| POST `/admin/accounts/{id}/reset-password` | `{ "password": "..." }` | 204 |

Assign 1–20 distinct roles. `GET /admin/permissions` returns `{code,label,group,description}[]`. `GET /admin/roles` accepts pagination and optional search; GET by ID, POST, PATCH by ID and DELETE by ID manage roles. POST/PATCH accept `{name,permissions,description?}` (name 1–64, description at most 500) and return `{id,name,description,isBuiltIn,permissions,userCount}`. An empty permission set is valid. A role with members cannot be deleted (`ROLE_IN_USE`); the built-in Admin role cannot be changed or deleted (`BUILT_IN_ROLE_PROTECTED`). Channel accounts cannot receive `platform.manage`.

Legacy account creation may provide singular `role` instead of `roleIds`; do not send both. The legacy account-access `permissions` field is adapted into explicit managed role assignments, not a second source of account grants. Migration preserves existing effective access, including formerly customized per-account permissions.

Usernames have at most 128 characters; passwords are 12–1024 characters and require uppercase, lowercase, digits and symbols. Passwords are neither returned nor audited. Password resets and account disable/enable invalidate previous cookies. Disabled accounts cannot sign in. Administrators cannot disable or downgrade themselves (`ACCOUNT_SELF_CHANGE_FORBIDDEN`, 409), scope restrictions on self/built-in Admin return `ACCOUNT_ACCESS_RESTRICTION_FORBIDDEN`, and changes cannot remove the last enabled built-in Admin account (`LAST_ADMIN_REQUIRED`, 409). Account mutations are serialized and recheck the actor's authority after obtaining the lock. Accounts are retained rather than deleted.

## Applications and plans

Application: `{ "id": "uuid", "code": "my-app", "name": "我的应用", "disabledAt": null, "createdAt": "..." }`. Application codes: 3–64 ASCII letters, numbers, `-` or `_`; unique and normalized lowercase.

Plan: `{ "id": "uuid", "applicationId": "uuid", "name": "月卡", "durationSeconds": 2592000, "maxDevices": 1, "isEnabled": true, "createdAt": "..." }`. Duration 1–315360000 seconds, devices 1–100; existing licenses retain the issued snapshot.

Plans additionally expose `maxConcurrentSessions` (1–100), `activationValidityDays` (null or 1–3650), and `features` (array). Create/update accept these fields; omitted online limit defaults to maxDevices, omitted activation days means unlimited, omitted features means empty. Existing plans/licenses migrate their online limit from their own maxDevices. Features are trimmed, lowercased, deduplicated, and match `[a-z][a-z0-9._-]{0,63}` with at most 50 distinct identifiers. All policy fields snapshot at issuance; later plan edits do not change issued licenses.

| Method and path | Request | Response |
| --- | --- | --- |
| GET `/admin/applications` | pagination | page of Application |
| POST `/admin/applications` | `{ "code": "my-app", "name": "我的应用" }` | 201 Application |
| POST `/admin/applications/{id}/disable` | no body | 204; revokes all sessions |
| POST `/admin/applications/{id}/enable` | no body | 204 |
| GET `/admin/applications/{id}/plans` | pagination | page of Plan |
| POST `/admin/applications/{id}/plans` | `{ "name": "月卡", "durationSeconds": 2592000, "maxDevices": 1 }` | 201 Plan |
| PATCH `/admin/plans/{id}` | `{ "name": "月卡", "durationSeconds": 2592000, "maxDevices": 1, "isEnabled": true }` (all fields required) | 200 GlobalPlanDto |

Plans are global records shared by explicit application references, without application overrides. Existing Plan IDs and license/order snapshots remain unchanged. The legacy application GET returns `PlanDto`: `applicationId` is the requested application and `isEnabled` combines application, global plan and reference state. The legacy POST atomically creates a global plan and its application reference.

| Method and path | Request | Response |
| --- | --- | --- |
| GET `/admin/plans` | page=1, pageSize=20 (max 100), search?, isEnabled? | page of GlobalPlanDto |
| POST `/admin/plans` | CreatePlan | 201 GlobalPlanDto, may be unbound |
| GET `/admin/plans/{id}` | none | GlobalPlanDto |
| GET `/admin/plans/{id}/applications` | none | array of PlanReferenceDto |
| GET `/admin/applications/{id}/plan-references` | pagination | page of ApplicationPlanDto |
| PUT `/admin/applications/{id}/plan-references/{planId}` | required `{ "isEnabled": true }` or false | ApplicationPlanDto; identical retries preserve first creation time and audit count |

`GlobalPlanDto` exposes ID, name, rules, global state, creation time and `referenceCount` (including paused references), with no single application ID. `ApplicationPlanDto` exposes application/plan IDs, name, rules, `planEnabled` (global), `isEnabled` (reference), `canIssue` (effective) and the first reference creation time. `PlanReferenceDto` exposes application ID/name, reference state and first creation time.

Global management reads/writes and reference maintenance require `platform.manage`, all application scope and no channel/customer restriction. Reference reads require `licenses.read` and visible application scope. Business catalogs retain their existing permissions. `ReportPlanDto` adds `isEnabled`; the same plan ID may appear under multiple applications. Historical queries retain disabled references; statistics group by batch application and plan without multiplying references.

New issuance/orders require enabled application, plan and reference. A paused reference returns `APPLICATION_PLAN_DISABLED` / 409; global pause returns `PLAN_DISABLED` / 403; an unreferenced/invisible pair returns 404. Existing pending orders fulfill saved snapshots after plan/reference pause, subject to existing customer/channel/application rules. Channel prices are unique by `channelId + applicationId + planId`; paused references may be priced, unbound pairs may not. Global `plan.create`/`plan.update` audits have no application ownership; `application-plan.bind`/`application-plan.update` retain the concrete application and before/after reference state.

Example creation: POST `/api/admin/plans` with `{ "name": "共享月卡", "durationSeconds": 2592000, "maxDevices": 1, "maxConcurrentSessions": 1, "activationValidityDays": 30, "features": ["export"] }`, then PUT each reference with `{ "isEnabled": true }`. Example issuance: POST `/api/admin/license-batches` with `{ "applicationId": "11111111-1111-4111-8111-111111111111", "planId": "22222222-2222-4222-8222-222222222222", "quantity": 1, "requestId": "33333333-3333-4333-8333-333333333333" }`. These GUIDs only illustrate the format.

## License issuance, lists and controls

`POST /admin/licenses/query` requires `licenses.read` and CSRF, but is a read operation for recent-MFA enforcement. It accepts `page`, `pageSize`, `applicationId`, `channelId`, `batchId`, `status`, `from`/`to` (issuance), `activatedFrom`/`activatedTo`, `expiresFrom`/`expiresTo`, `note`, `tag`, and `deviceId`; all nonempty filters combine with AND. Time ranges are UTC, lower-inclusive and upper-exclusive. Reversed bounds return 400; equal bounds yield an empty interval. `deviceId` is an exact match against the existing application-scoped SHA-256 binding digest and includes released bindings. It requires a visible `applicationId`; raw device identifiers are neither returned nor persisted. Response is the normal page of License. The existing GET list remains available.

## Operations reports

`GET /admin/reports/operations?from=&to=&applicationId=&planId=&channelId=` requires `reports.read`. Defaults are today plus the preceding 29 UTC dates; maximum range is 366 days. Response is `{from,to,generatedAt,timeZone:"UTC",totals,daily,breakdown}`. `totals` has integer `issued`, `activated`, `expired`, `onlineLicenses`, and `onlineSessions`. Daily rows have `date,issued,activated,expired`, including zero days. Breakdown rows add `applicationId,applicationName,planId,planName` to the five totals; trial plan fields are null.

Issued and first-activation counts use durable license facts. Expired means the current expiry falls in the interval and has passed, excluding frozen licenses; it does not reconstruct historical expiry dates before renewal. Online requires enabled application/license, no freeze, unreleased binding, unrevoked session, and valid lease plus absolute deadlines. Online licenses are deduplicated. Filters and scope apply before aggregation. The dashboard refreshes these reports every 60 seconds and labels the UTC basis.

`GET /admin/reports/catalog` requires `reports.read`, returning `{applications:[{id,name}],plans:[{id,applicationId,name}]}` for all visible entries, including disabled historical entries. Report-only accounts can use it without `licenses.read`.

## License data and mutations

Batch: `{ "id": "uuid", "applicationId": "uuid", "planId": "uuid", "quantity": 10, "createdBy": "uuid", "createdAt": "...", "note": "...", "requestId": "uuid" }`.

License summary: `{ "id": "uuid", "applicationId": "uuid", "batchId": "uuid", "keyHint": "V1-…ABCD", "durationSeconds": 2592000, "maxDevices": 1, "status": "unused", "createdAt": "...", "activatedAt": null, "expiresAt": null, "disabledAt": null, "note": null, "tags": [] }`. Additional policy fields are `maxConcurrentSessions`, `activateBefore`, `frozenAt`, `frozenRemainingSeconds` (nullable number), `features` and `isTrial`. Trial licenses have null batchId. Status values `unused`, `active`, `frozen`, `activation_expired`, `expired`, `disabled`; disabled takes precedence over frozen. Application disabled state is separate. All status values can be used as list filters.

Unactivated licenses reject activation at or after activateBefore with 403 `ACTIVATION_DEADLINE_PASSED`. Timely activation starts the full issued duration independently of this deadline. Renewal never changes activateBefore; a missed deadline requires replacement issuance.

License detail: `{ "license": LicenseSummary, "bindings": [Binding] }`.

Binding: `{ "id": "uuid", "deviceLabel": "工作电脑", "boundAt": "...", "releasedAt": null, "sessions": [Session] }`.

Session: `{ "id": "uuid", "createdAt": "...", "absoluteExpiresAt": "...", "leaseExpiresAt": "...", "lastSeenAt": "...", "revokedAt": null }`.

| Method and path | Request | Response |
| --- | --- | --- |
| POST `/admin/license-batches` | `{ "applicationId": "uuid", "planId": "uuid", "quantity": 10, "note": "...", "requestId": "uuid" }` | 201 `{ "batch": Batch, "licenses": [{ "id": "uuid", "key": "V1-...", "keyHint": "V1-…ABCD" }] }`; no-store, plaintext once |
| GET `/admin/license-batches` | pagination; optional `applicationId`, `channelId`, `requestId` (current actor when requestId supplied) | page of Batch; no keys |
| POST `/admin/license-batches/{id}/disable` | no body | 204 |
| GET `/admin/licenses` | pagination; optional `applicationId`, `channelId`, `batchId`, `status`, `from`, `to` (createdAt, inclusive from, exclusive to), `note` (substring), `tag` (exact match) | page of LicenseSummary |
| POST `/admin/licenses/search` | `{ "licenseKey": "V1-..." }` | 200 LicenseDetail, 404 if absent; no-store |
| GET `/admin/licenses/{id}` | — | LicenseDetail |
| POST `/admin/licenses/{id}/disable` | no body | 204 |
| POST `/admin/licenses/{id}/enable` | no body | 204 |
| POST `/admin/device-bindings/{id}/release` | no body | 204; retains license expiry, revokes binding sessions |
| POST `/admin/sessions/{id}/revoke` | no body | 204 |

Quantity 1–1000. Names 1–120 characters, note at most 500 characters. Request IDs must be nonempty UUIDs. Issuance retry must reuse the same request ID and payload. On 409 `BATCH_ALREADY_CREATED`, or after a network loss, find the batch by requestId, disable it, then issue with a new request ID. The server cannot recover old plaintext.

## Batch overview and expiry

| Method and path | Query | Response |
| --- | --- | --- |
| GET `/admin/batch-overview` | pagination; optional `applicationId`, `channelId`, `planId`, `createdBy`, `search` (batch note) | page of BatchOverview |
| GET `/admin/batch-overview/{id}` | — | BatchOverview |
| GET `/admin/expiring-licenses` | pagination; `withinDays` (3, 7 or 30; default 7), optional `applicationId`, `channelId` | `{ "items": [LicenseSummary], "total": 0, "page": 1, "pageSize": 20, "serverTime": "...", "withinDays": 7 }` |

BatchOverview adds `applicationName`, `planName`, `createdByName`, `activatedCount` and `disabledCount` to Batch. Counts reflect current licenses; activated means `activatedAt` is present, so activated and disabled counts can overlap. Neither batch endpoint returns plaintext keys. Whole-batch disable continues to use `/admin/license-batches/{id}/disable`.

Expiry results contain activated, enabled, unfrozen licenses belonging to enabled applications, ordered by expiry ascending. Frozen licenses also do not count as valid licenses on the dashboard. The server-time window is `serverTime < expiresAt <= serverTime + withinDays * 24 hours`. Renewal refreshes the list; an out-of-range page returns to the last available page.

## Renewal, metadata and bulk operations

`POST /admin/license-operations` requires `licenses.manage` and returns 200 OperationResult. Channel-owned licenses must be renewed through an order, including for platform staff (`CHANNEL_ORDER_REQUIRED`); other permitted management actions remain available. Request:

```json
{
  "requestId": "nonempty-uuid",
  "licenseIds": ["license-uuid"],
  "action": "extend",
  "additionalSeconds": 3600,
  "reason": "Customer renewal"
}
```

`licenseIds` must contain 1–100 distinct nonempty UUIDs. `action` is `disable`, `enable`, `extend` or `metadata`; `reason` is required and at most 500 characters after trimming. Only `extend` accepts `additionalSeconds`: 3600–315360000, in multiples of 3600. Only `metadata` accepts `note` and `tags`; both are whole replacements, and absent/empty values clear the old values. Notes have at most 500 characters. Tags contain at most 10 entries of 1–32 characters, trimmed and deduplicated with case-sensitive matching.

Renewal rules:

- Unactivated licenses add to the pending activation duration, capped at 315360000 seconds total.
- Active licenses extend the existing expiry; expired licenses extend from server current time. The resulting expiry must be within 315360000 seconds of current time. Historical activation time and issued duration snapshot are retained for activated licenses.
- Disabled licenses remain disabled; disabled applications remain disabled. Device bindings remain. Revoked or expired sessions are not restored, and existing session absolute expiry is not extended.
- Disable revokes existing sessions. Enable does not extend expiry or restore old sessions.
- Frozen licenses cannot be renewed (`LICENSE_FROZEN` per-item failure), including when also disabled.

OperationResult: `{ "requestId": "uuid", "action": "extend", "completedAt": "...", "succeeded": 1, "failed": 0, "items": [{ "licenseId": "uuid", "success": true, "errorCode": null, "license": LicenseSummary }] }`. Missing licenses (`NOT_FOUND`) and duration-limit failures (`DURATION_LIMIT_EXCEEDED`) produce per-item failures with `license: null`; other valid items can succeed. Invalid top-level parameters reject the request. Unexpected database errors roll back the entire operation.

Results are persisted under `(actorId, requestId)`. A retry with the same normalized payload returns the original result without repeating any change. Changed parameters return 409 `IDEMPOTENCY_CONFLICT`. `GET /admin/license-operations/{requestId}` returns only the current account's receipt, or 404 when absent. Application → License → Binding → Session locks are acquired in a consistent order across applications.

The UI stores only the immutable pending request in account-scoped sessionStorage before sending. After an ambiguous response, retain the original request ID and payload, including when a later retry receives a rejection; query the receipt or retry the original request. Successful receipt recovery clears pending state. This recovery differs from issuance because operation results contain no plaintext keys.

## Device rebinding

One rebinding event means successfully releasing one active binding. It revokes all sessions for that binding and frees a slot for later activation without changing license expiry. Repeating an admin release for an already released binding is 204 without consuming another event. Client release requires a currently valid session, so its revoked token cannot be reused.

New licenses default to three changes in a rolling 30-day window, with 24 hours between successful releases. Existing licenses retain unrestricted behavior. Configuration applies per license; changing policy does not clear history. Disabled policy still records releases. Windows use server time `(now - windowDays, now]`; release is allowed exactly when cooldown ends. Independent history survives audit retention.

| Method and path | Request / authorization | Response |
| --- | --- | --- |
| GET `/admin/licenses/{id}/device-policy` | all read roles | DevicePolicy |
| PATCH `/admin/licenses/{id}/device-policy` | `licenses.manage`; `{ "enabled": true, "maxChanges": 3, "windowDays": 30, "cooldownHours": 24, "reason": "..." }` | DevicePolicy |
| POST `/admin/licenses/{id}/device-policy/reset` | `licenses.manage`; `{ "reason": "..." }` | DevicePolicy; fresh counter generation, previous events retained |
| POST `/admin/device-bindings/{id}/release` | `licenses.manage`; no body | 204; applies the policy |
| POST `/client/device-bindings/release` | Bearer current session; no body | 204; only releases the token's own binding |

DevicePolicy: `{ "licenseId": "uuid", "enabled": true, "maxChanges": 3, "windowDays": 30, "cooldownHours": 24, "usedChanges": 0, "remainingChanges": 3, "lastChangedAt": null, "nextAllowedAt": null, "serverTime": "..." }`. Remaining changes are null for unrestricted policies. Limits: maxChanges 1–100, windowDays 1–365, cooldownHours 0–8760, reason 1–500 characters after trimming. Quota exhaustion returns 409 `REBIND_LIMIT_REACHED`; cooldown returns 409 `REBIND_COOLDOWN`. Reset clears current quota/cooldown using a new generation, does not alter binding/expiry or revive any session, and writes an audit. Existing parent-first locking serializes release, reset, configuration and activation.

## Freeze and trial policies

| Method and path | Request / authorization | Response |
| --- | --- | --- |
| POST `/admin/licenses/{id}/freeze` | `licenses.manage`; `{ "reason": "暂停使用" }` | 204 |
| POST `/admin/licenses/{id}/unfreeze` | `licenses.manage`; `{ "reason": "恢复使用" }` | 204 |
| GET `/admin/licenses/{id}/freeze-history` | Read; standard pagination | page of LicenseFreeze |
| GET `/admin/applications/{id}/trial-policy` | Read | TrialPolicy |
| PUT `/admin/applications/{id}/trial-policy` | `platform.manage`; `{ "isEnabled": true, "durationSeconds": 3600, "features": ["preview"] }` | TrialPolicy |
| POST `/client/trials/activate` | public; `{ "appCode": "my-app", "deviceId": "persistent-id", "deviceLabel": "Desktop" }` | same grant as paid activation, with isTrial true |

Freeze/unfreeze reasons contain 1–500 characters after trimming. Only enabled, already activated, unexpired licenses can first freeze; otherwise 403 `LICENSE_DISABLED` or `LICENSE_NOT_FREEZABLE`. Freeze revokes sessions and returns `LICENSE_FROZEN` to activation/heartbeat. Repeated freeze or unfreeze does not shift the clock again. Unfreeze restores the exact remaining duration from that instant; it never clears disable state or restores old sessions. Independent LicenseFreeze records survive audit retention and contain `id`, `licenseId`, `frozenAt`, `unfrozenAt`, `remainingSeconds`, `frozenBy`, `unfrozenBy`, `freezeReason`, `unfreezeReason`.

TrialPolicy contains `applicationId`, `isEnabled`, `durationSeconds`, `features`. Default is disabled, 3600 seconds and no features; duration bounds are 1–2592000 seconds. Changes affect first claims made afterward. Disabling claims does not revoke existing trial sessions. Trials have one device and one online session, no plaintext key, and use the normal heartbeat/logout/release protocols. Retrying on the same device rotates the session without extending the trial. Per-application/device-digest claims are permanent; expired/upgraded claims cannot reset, even after audit cleanup. A device with a historical formal binding cannot first claim a trial. Denials are 403 `TRIAL_DISABLED`, `TRIAL_EXPIRED` or `TRIAL_UPGRADED`.

Use the ordinary paid `/client/licenses/activate` endpoint with the same app/device to upgrade a trial. Successful paid activation records the relationship and revokes all trial sessions atomically; failed paid activation leaves the trial intact. No separate upgrade token or endpoint is required. Trial and formal activations share an application/device lock and ordered license locks, and share the application/IP admission bucket. Device identifiers are client-provided; digest deduplication is not hardware attestation.

## Audit

`GET /admin/audit-logs`: pagination and optional `category` (`operation` or `login`), `actorId`, `actorName`, `targetId`, `action`, `from`, `to`. Omitting category preserves the combined historical interface. `GET /admin/login-logs` is the separate login-only query. Both require `audit.read` and apply resource scope before counts and pagination.

Audit records retain `{id,actorId,action,targetId,changes,createdAt,traceId}` and add `{category,result,actorName,targetType,targetName,applicationId,applicationName,sourceIp,method,errorCategory}`. Login records distinguish successful/failed password or MFA authentication and logout without retaining credentials. Historical missing facts remain null; they are not fabricated. Display names resolve only within visible resources. Safe `changes` can contain real `{before,after}` snapshots; records without an old snapshot must be displayed as recorded facts rather than invented comparisons.

Actions: `application.create`, `application.disable`, `application.enable`, `plan.create`, `plan.update`, `batch.create`, `batch.disable`, `license.disable`, `license.enable`, `license.extend`, `license.metadata`, `binding.release`, `session.revoke`, `account.create`, `account.role`, `account.disable`, `account.enable`, `account.reset-password`, and CLI actions `admin.create`, `admin.reset-password`. License operations record request ID, reason, success/failure and safe before/after summaries per item. Audit changes contain no passwords, plaintext keys, device IDs, or token digests.

Security/rebinding actions: `security.login`, `security.login-two-factor`, `security.login-failed`, `security.logout`, `security.password`, `security.two-factor-setup`, `security.two-factor-enable`, `security.two-factor-disable`, `security.recovery-codes`, `security.verify`, `license.device-policy.update`, `license.device-policy.reset`. Role actions include `role.create`, `role.update`, `role.delete`, and `account.roles`. Authenticator secrets, challenges, recovery codes and personal contact details are never audited.

Policy actions: `license.freeze`, `license.unfreeze`, `application.trial-policy.update`. Freeze history is separate from these ordinary audit entries.

## Customers and manual orders

Payments are deferred. Prices are integer CNY cents for manual settlement records. Public quota balances and order totals stay within `9_007_199_254_740_991`; unit price is at most `90_071_992_547_409` cents and quantity is 1–100.

Customer management requires `customers.manage`. Customer records contain `{id,applicationId,channelId,name,email,phone,note,createdAt,updatedAt,disabledAt,anonymizedAt,hasAccessCode}`. Name is 1–120 characters; optional email/phone/note are at most 254/40/500. Application and channel ownership cannot be edited after creation.

| Method and path | Request / filters | Response |
| --- | --- | --- |
| GET `/admin/customers` | pagination, `applicationId`, `channelId`, `search` | Page of customers |
| POST `/admin/customers` | `{applicationId,name,email?,phone?,note?,channelId?,requestId?}` | 201 Customer |
| GET/PATCH `/admin/customers/{id}` | PATCH `{name,email?,phone?,note?}` | Customer |
| POST `/admin/customers/{id}/disable`, `/enable`, `/anonymize` | no body | Customer |
| POST `/admin/customers/{id}/rotate-access-code` | no body | `{customer,accessCode}`; one-time secret |
| GET `/admin/customers/{id}/licenses` | pagination | Page of linked licenses |

Anonymization clears name and contacts and revokes portal access, retaining non-identifying business relationships. Access-code rotation, disable and anonymization immediately revoke existing portal sessions. The code contains 256 random bits and only its digest is stored.

Order management requires `orders.manage`, independently of customer-contact and direct licensing permissions. `GET /admin/orders/catalog?customerId=...` returns scoped `{customers:[{id,applicationId,channelId,name}],licenses:[{id,customerId,keyHint}]}` for order forms without granting contact access.

| Method and path | Request / filters | Response |
| --- | --- | --- |
| GET `/admin/orders` | pagination, `applicationId`, `channelId`, `customerId`, `status`, `requestId` | Page of orders |
| POST `/admin/orders` | `{requestId,applicationId,customerId,planId,type,quantity,unitPriceCents,licenseId?,additionalSeconds?}` | 201 Order |
| GET `/admin/orders/{id}` | — | Order with immutable `snapshot`, linked `licenseIds`, `history` and delivery availability |
| POST `/admin/orders/{id}/fulfill` | `{requestId}` | Fulfilled order |
| POST `/admin/orders/{id}/cancel` | no body | Cancelled order; pending only |
| GET `/admin/orders/{id}/delivery` | — | `{orderId,expiresAt,licenses:[{id,key,keyHint}]}` |

Types are `issue` and `renewal`; states are `pending`, `fulfilled`, `cancelled`. Renewal requires quantity 1, a formal license belonging to the customer, and a positive integer-hour `additionalSeconds`. Frozen licenses cannot be renewed. Order snapshots preserve plan name, duration, device/online limits, activation window and features. Channel pricing overrides the submitted unit price.

Issue/renewal, quota debit, immutable quota entry and durable events commit in one transaction. Repeated fulfillment cannot create another batch, extend again or debit again. Save a request ID before sending; an ambiguous creation response can be recovered through the `requestId` list filter. Reusing a request ID with changed content returns 409.

Only dedicated delivery endpoints expose plaintext keys. The server stores delivery content encrypted using its persistent Data Protection key ring, retaining it for 30 days. Expired or missing delivery returns 410 `DELIVERY_UNAVAILABLE`; ordinary order/license lists and events never include keys. Issuance outside orders remains digest-only.

## Channels

All channel administration requires `platform.manage`. Channel records contain `{id,name,accountId,applicationIds,remainingQuota,isEnabled,createdAt,updatedAt,prices}`. A channel binds one enabled account without platform-management grants. Account application scope and channel application assignments both apply.

| Method and path | Request / filters | Response |
| --- | --- | --- |
| GET `/admin/channels` | pagination, `search` | Page of channels |
| POST `/admin/channels` | `{name,accountId,applicationIds,initialQuota}` | 201 Channel |
| GET/PATCH `/admin/channels/{id}` | PATCH `{name,accountId,applicationIds,isEnabled}` | Channel |
| POST `/admin/channels/{id}/quota-adjustments` | `{requestId,delta,reason}` | Channel; nonzero delta and required reason |
| GET `/admin/channels/{id}/quota-ledger` | pagination | Immutable entries with `delta`, `balanceAfter`, reason, actor, time, request/order references |
| PUT `/admin/channels/{id}/prices` | `{applicationId,planId,unitPriceCents}` | Channel price |

Each issued formal key consumes one quota unit; a renewal order consumes one. Balances cannot fall below zero. Adjustment retries retain both original request ID and body. Channel staff use order fulfillment and cannot bypass quota with direct issuance or extension. Disabling a channel blocks new channel business while preserving already-issued client authorizations.

## Customer portal

The `/portal` web root uses separate authentication and query caches. Portal requests use `credentials: 'omit'` and an independent Bearer token; administrator cookies grant no portal access. `POST /portal/sessions {accessCode}` returns `{token,expiresAt,customer}` for an eight-hour session. Only the token digest is stored server-side; the web client stores the token in the current browser session. `DELETE /portal/sessions` revokes it.

Authenticated portal endpoints are GET `/portal/me`, paged GET `/portal/orders`, GET `/portal/orders/{id}`, GET `/portal/orders/{id}/delivery`, paged GET `/portal/licenses`, GET `/portal/licenses/{id}`, and paged GET `/portal/requests`. Every result is restricted to that customer, application and channel; other customer IDs return 404.

POST `/portal/requests` accepts `{requestId,type,licenseId,bindingId?,note?}`. Type is `renewal` or `rebinding`; rebinding identifies the customer's unreleased binding. The result records `pending`, `approved` or `rejected` plus review time/reason and any linked order.

Staff with `orders.manage` use paged GET `/admin/portal-requests` with optional application/channel/customer/status filters, and POST `/admin/portal-requests/{id}/review {status,reason,planId?,unitPriceCents?,additionalSeconds?}`. Approval requires a reason. Renewal approval creates and fulfills one renewal order using the supplied plan/hours; rebinding approval follows existing cooldown/quota rules and revokes the binding's sessions. Repeated identical review does not repeat the effect.

## External business API credentials

`platform.manage` manages `/admin/api-credentials`: paged GET (optional application/channel), POST `{applicationId,name,permissions,channelId?}`, PATCH `/{id} {name,permissions,isEnabled}`, and POST `/{id}/rotate`. Creation and rotation return `{credential,secret}` once; the credential DTO never contains the secret, and storage retains only its digest. Creation is enabled by default.

Allowed grants are the explicit subset of `licenses.read`, `customers.manage`, `orders.manage`. Call `/v1` with `Authorization: Bearer <secret>` from the merchant's server. The software client uses `/client`, not these credentials. Rotating/disabling a credential invalidates its old value immediately, including pending writes before commit. Credentials cannot manage accounts, backups or their own permissions.

Available business endpoints are GET `/v1/catalog`; GET `/v1/licenses` and `/{id}`; GET/POST `/v1/customers`, GET/PATCH `/v1/customers/{id}`, GET `/v1/customers/{id}/licenses`; GET/POST `/v1/orders`, GET `/v1/orders/catalog`, GET `/v1/orders/{id}`, POST `/v1/orders/{id}/fulfill`, GET `/v1/orders/{id}/delivery`. DTOs match their administrative equivalents. External customer creation and PATCH require a nonempty `requestId`, as do order creation and fulfillment. Requests are scoped to the credential application/optional channel before counts and ID lookup. Changed content under an existing request ID returns 409.

Each credential has a 60-request/minute process-local limit in addition to the IP/global limiter. Multi-instance deployments must enforce the shared credential quota at the gateway; routing each credential to one instance is the alternative. Standard 429 responses include `Retry-After`.

## Software clients

`POST /client/licenses/activate`: `{ "appCode": "my-app", "licenseKey": "V1-...", "deviceId": "persisted-random-device-id", "deviceLabel": "工作电脑" }`. Device ID 1–256 characters, optional label maximum 120. Returns 200 `{ "sessionToken": "...", "serverTime": "...", "expiresAt": "...", "leaseExpiresAt": "...", "absoluteExpiresAt": "...", "heartbeatIntervalSeconds": 5 }` with no-store. Repeated activation retains first expiry and revokes the previous session for that binding.

`POST /client/sessions/heartbeat` (no body), header `Authorization: Bearer <sessionToken>`. Returns 200 `{ "serverTime": "...", "expiresAt": "...", "leaseExpiresAt": "...", "absoluteExpiresAt": "...", "heartbeatIntervalSeconds": 5 }`. Lease 180 seconds, absolute session 8 hours, both capped by license expiry. Lease must still be valid when heartbeat arrives; exact expiry is invalid. Explicit errors require the client to stop protected functions. Network loss permits retries only within the last lease using a monotonic clock.

`POST /client/sessions/logout` (no body), same bearer header: 204, revokes session and preserves device binding. Client tokens never grant admin access. Activation/heartbeat reject `APPLICATION_DISABLED`, `LICENSE_INVALID`, `LICENSE_DISABLED`, `LICENSE_EXPIRED`, `DEVICE_LIMIT_REACHED`, `BINDING_RELEASED`, `SESSION_INVALID`, `SESSION_REVOKED`, `SESSION_EXPIRED` as applicable.

Activation and heartbeat additionally return `licenseId`, `isTrial` and `features`; original fields retain their behavior. A client should enable a named capability only when its current monotonic lease is valid and features contains the exact canonical identifier. Missing/empty features grant no named capabilities. This is an online TLS response, not an offline signed license.

Online quota is independent of bound-device quota. An online session has an unreleased binding, no revocation, and both lease and absolute expiry strictly in the future. Same-device replacement does not consume an extra slot. New-device activation beyond maxConcurrentSessions returns 403 `SESSION_LIMIT_REACHED` without creating a binding or starting license time; if both quotas are exhausted the original `DEVICE_LIMIT_REACHED` takes precedence. Concurrent activation cannot oversubscribe either quota.

## Client versions

| Method and path | Request / access | Response |
| --- | --- | --- |
| GET `/admin/applications/{id}/version-policy` | `platform.manage` OR `licenses.read`, with current application scope | Version policy |
| PUT `/admin/applications/{id}/version-policy` | `platform.manage`; body contains the six policy fields below | Saved version policy |
| GET `/client/applications/{appCode}/version` | Public; enabled application | Public version policy |

Policy fields are `minimumVersion,latestVersion,enforceMinimum,updateUrl,releaseNotes,sha256`. The administrative DTO also has `applicationId`; the public DTO instead has `applicationCode`. Defaults are null values and `enforceMinimum=false`.

Versions are strict three-part numbers, each 0–9999. A non-null minimum requires a latest version greater than or equal to it; enforcement requires a minimum. Update URL is HTTPS, at most 2,048 characters, with no embedded credentials. Only Development/Testing with explicit `ClientVersions:AllowLoopbackHttp=true` permits loopback HTTP. Release notes are at most 4,000 characters; SHA-256 is null or exactly 64 hexadecimal characters.

Activation and trial request bodies accept optional `clientVersion`. The session retains it; heartbeat can update it with `X-Client-Version`, and omission retains the session version. Each request checks the current application policy. With enforcement enabled, missing version gives `CLIENT_VERSION_REQUIRED` and insufficient version gives `CLIENT_UPGRADE_REQUIRED`, both HTTP 426; malformed versions give `CLIENT_VERSION_INVALID` (400). Existing sessions receive the same live policy check.

Successful activation/heartbeat and 426 version errors contain `version`: `{clientVersion,minimumVersion,latestVersion,enforceMinimum,upgradeAvailable,updateUrl,releaseNotes,sha256}`. An optional update never extends the lease or license. Publishing update information does not download or execute an installer; clients verify the publisher signature and configured digest before installation.

## Offline authorization

| Method and path | Request / access | Response |
| --- | --- | --- |
| GET `/admin/offline-licenses/configuration` | `licenses.read` | `{enabled,configured,currentKeyId,publicKeys,errorCode,maxHours,defaultHours,limitations}` |
| GET `/admin/offline-licenses` | `licenses.read`; optional `applicationId,licenseId` and pagination | Page of scoped metadata |
| POST `/admin/offline-licenses` | `licenses.manage`, current scope and existing sensitive-operation policy; `{licenseId,bindingId,deviceId,hours?}` | 201 `{record,licenseContent}` |
| POST `/admin/offline-licenses/{id}/revoke` | `licenses.manage`; no body | 204 |
| POST `/client/offline-licenses/verify` | Public; `{licenseContent,appCode,deviceId}` | Verified identity, features, validity, server time and current status |

Metadata fields are `id,serial,applicationId,applicationCode,licenseId,licenseHint,bindingId,deviceLabel,kid,features,issuedAt,notBefore,expiresAt,revokedAt,revocationReason,issuedBy,status,limitations`. Status is `active,expired,revoked,superseded,unavailable`. Normal metadata responses contain neither signed content nor raw device IDs; public key configuration contains no private keys.

Signing requires an enabled application, formal activated usable license, and an unreleased binding whose stored digest matches the supplied raw device ID. Duration is an integer 1–168 hours (default 24), capped by license expiry. Signing again for the same binding supersedes its prior active record. Signed content is returned only by the signing response, then kept in the frontend component's temporary memory until the download dialog is closed. A lost response cannot recover that content from a list endpoint; inspect records before deciding whether to sign a replacement.

The signed file is ES256 compact JWS, with header `{alg:"ES256",typ:"KG-OFFLINE",kid}` and IEEE P1363 signature bytes. Payload is `{formatVersion,serial,applicationCode,licenseId,bindingId,features,issuedAt,notBefore,expiresAt,deviceHash}`. `deviceHash` is SHA-256 of UTF-8 `normalizedAppCode + "\n" + rawDeviceId`, distinct from the server's original binding digest.

Configure `Offline:CurrentKeyId` and `Offline:Keys:{kid}` with the current PKCS#8 P-256 private key PEM. Retained old IDs can hold SPKI public key PEM. Missing or invalid configuration prevents signing; `Offline:Enabled=false` disables server signing and verification. Key rotation distributes the new public key through a trusted channel before switching the current signing ID. Preserve old public keys until old licenses expire. Encrypted backup includes the Offline configuration.

The exported trusted-key JSON is a flat mapping from kid to SPKI public PEM. The C# verifier accepts this trusted configuration separately and does not trust keys embedded in a license. It validates signature, header, format version, application/device identity, validity and required features. Every protected operation checks time and features again. Offline mode uses a monotonic process clock and a persisted trusted UTC floor across restarts; rollback exceeding five minutes or a failed state write denies protected use. See the C# sample README for the three `KEYVALIDATION_OFFLINE_*` file settings.

**Offline revocation delay:** a disconnected device cannot immediately learn about revocation, superseding, freezing, disabling or unbinding. Its maximum remaining access equals the certificate's remaining validity. Online verification checks signature plus the database record and current application/license/binding state. Without trusted hardware or network time, local snapshot rollback and executable patching remain outside the offline protection guarantee.

## Risk and notifications

`GET /admin/catalog` returns the same minimal application/plan shape as the report catalog, with current scope applied. It requires an authenticated read role and at least one effective operation permission, so risk/customer/order-only users can select applications. Accounts with no permissions receive 403.

| Method and path | Request / filters | Response / access |
| --- | --- | --- |
| GET/PUT `/admin/applications/{id}/risk-policy` | PUT `{enabled,activationThreshold,activationWindowMinutes,rebindingThreshold,rebindingWindowMinutes,regionWindowHours}` | Same fields plus `applicationId`; `platform.manage` |
| GET `/admin/risk-events` | pagination, `applicationId`, `status` | Page of risk records; `risk.review` |
| GET `/admin/risk-events/{id}` | — | Risk record; `risk.review` |
| POST `/admin/risk-events/{id}/review` | `{status,reason}` | Updated risk record; `risk.review` |
| POST `/admin/operations/scan` | no body | `{scannedAt,riskEventsCreated,eventsDispatched,notificationsCreated,deliveriesCreated}`; `platform.manage` |
| GET/PUT `/admin/applications/{id}/notification-policy` | PUT `{enabled,recipientIds,eventTypes,expiryDays}` | Same fields plus `applicationId`; `platform.manage` |
| GET `/admin/notifications` | pagination, `applicationId`, `unreadOnly` | Current recipient's scoped notification page |
| POST `/admin/notifications/{id}/read` | no body | 204; current recipient only |

Risk rules are `activation_burst`, `rebinding_burst`, and `country_change`. Defaults are enabled, 10 activations/10 minutes, 3 releases/60 minutes and a 24-hour country window. Positive maximums are 10,000 activations, 1,440 activation-window minutes, 1,000 releases, 10,080 release-window minutes and 720 country-window hours. Risk records contain `id,applicationId,licenseId,rule,summary,evidenceJson,windowStart,windowEnd,status,createdAt,reviewedAt,reviewedBy,reviewReason`. Status is `open`, `confirmed`, or `dismissed`; review requires a nonblank reason (at most 1,000 characters). The scanner records evidence and never automatically disables a license.

Country evidence is unavailable by default. Only a direct proxy explicitly listed in `Risk__TrustedCountryProxies` can supply the two-letter `X-KeyGate-Country` header; checking occurs before forwarded-header processing. `Proxy__KnownProxies` alone does not authorize country evidence. Do not forward an untrusted incoming country header from an edge proxy.

Notification events are `license.expiring`, `risk.detected`, `batch.completed`, `service.degraded`, `service.recovered`, `order.fulfilled`, `license.renewed`, and `binding.released`. `expiryDays` is a subset of `[3,7,30]`. Recipients must be enabled staff who can access the application; invalid recipients return `INVALID_NOTIFICATION_RECIPIENT` (400). Notification records have `id,applicationId,eventType,title,message,readAt,createdAt,resourceId`. Reads remain recipient- and application-scoped after access changes. Expiry deduplication includes the current expiry timestamp, allowing a later reminder after renewal.

## Signed Webhooks

All Webhook management routes require `integrations.manage`; `platform.manage` alone does not grant access. `GET /admin/webhook-subscriptions` accepts normal pagination and `applicationId`; POST accepts `{applicationId,name,url,eventTypes,isEnabled}`. `PATCH /admin/webhook-subscriptions/{id}` accepts `{name,url,eventTypes,isEnabled}`. `POST /admin/webhook-subscriptions/{id}/rotate-secret` rotates the secret. Create returns 201 and rotation returns 200, both `{subscription,secret}`. Normal subscription fields are `id,applicationId,name,url,eventTypes,isEnabled,createdAt,updatedAt`, with no secret.

The one-time secret is Base64 encoding of 32 random bytes. Decode it before calculating `hex(HMAC-SHA256(secretBytes, timestamp + "\n" + rawBody))`. Headers are `X-KeyGate-Event-Id`, `X-KeyGate-Timestamp`, and `X-KeyGate-Signature`. Retry preserves event ID and raw body; delivery is at least once and unordered. Receivers must validate signatures and deduplicate event IDs. Payloads contain safe resource references, never license keys, access codes or private contact data.

`GET /admin/webhook-deliveries` accepts pagination, `applicationId`, `subscriptionId`, and `status`. Rows are `{id,eventId,subscriptionId,eventType,status,attempts,nextAttemptAt,completedAt,lastError,createdAt}`. Status is `pending`, `delivering`, `succeeded`, `failed`, or `expired`. Automatic attempts: immediately, then after 1/5/30/120 minutes, at most 5 attempts and 24 hours. Only 2xx succeeds. `POST /admin/webhook-deliveries/{id}/retry` requeues the same event with a new retry budget; an active delivery lease returns `DELIVERY_IN_PROGRESS` (409). Errors retain categories rather than response bodies.

Configure `Webhooks__AllowedHosts__0` (and further indexed hosts). Production requires HTTPS, actual connected public IPs, no redirects, bounded timeout and response size. `Webhooks__TimeoutSeconds` defaults to 10 (1–30); `Webhooks__MaxResponseBytes` defaults to 65,536 (1,024–1,048,576). Only Development/Testing with explicit `Webhooks__AllowLoopbackHttp=true` allows loopback HTTP fixtures. Invalid destinations return `WEBHOOK_TARGET_BLOCKED` (400).

## Encrypted backups and monitoring

All endpoints below require `platform.manage`, CSRF for mutations and the existing recent-MFA policy for sensitive actions.

| Method and path | Result |
| --- | --- |
| GET `/admin/backups/status` | `{enabled,configured,scheduleReady,intervalHours,retentionCount,maxArchiveBytes,configurationErrors,lastSuccessfulAt,nextScheduledAt}` |
| GET `/admin/backups` | Normal page of backup jobs |
| POST `/admin/backups` | 201 backup job after the synchronous attempt; inspect `status`, since a recorded failed job also returns 201 |
| POST `/admin/backups/{id}/verify` | `{backupId,status,completedAt,durationMilliseconds,migrationCount,keysReadable,errorCode}` after actual isolated restore |
| GET `/admin/monitoring` | `{generatedAt,databaseAvailable,latest,samples,incidents,workerErrors}` |
| POST `/admin/monitoring/sample` | Actual monitoring sample |
| GET `/admin/monitoring/metrics` | Prometheus text (`text/plain; version=0.0.4`) |

Backup jobs expose `id,status,createdAt,completedAt,sizeBytes,sha256,verifiedAt,verificationStatus,verificationDurationMilliseconds,errorCode,verificationErrorCode`. Job status is `running/succeeded/failed`; verification is `not_verified/running/succeeded/failed`. No archive path, password or key material is returned by these HTTP endpoints. Verification creates a fresh `keyvalidation_restore_...` database, restores via native pg_restore, validates tables/migrations/keys, then removes only that isolated database. Disaster recovery and configuration are documented in [backup and restore](backup-restore.md).

Monitoring samples have `id,sampledAt,windowStart,windowEnd,databaseAvailable,requestCount,serverErrorCount,errorRate,p95LatencyMs,routes`. Each route has the fixed route template and the same four request/error/latency measurements. Rate and P95 are null when no traffic exists. Incident fields are `id,kind,summary,startedAt,resolvedAt`; worker errors contain only `worker,errorCode,occurredAt`. Sampling is every 60 seconds. Database failure opens an incident immediately; at least 20 requests and error rate ≥5% or P95 ≥500ms in three consecutive samples open threshold incidents. Recovery resolves the same incident; outage state is retained in memory until the database can persist it. Samples retain 30 days, incidents/notifications 180 days, and cleanup preserves unfinished deliveries.

## Operations

Live OpenAPI: `/openapi/v1.json`. Liveness: `/health/live`; readiness (database): `/health/ready`.

Required environment: `ConnectionStrings__Database`, `Licensing__CurrentKeyVersion=v1`, `Licensing__Keys__v1=<base64 32+ random bytes>`. Keep old key versions configured until their licenses retire. Optional `DataProtection__Path`, `Cors__Origins__0=http://localhost:5173`, `Retention__AuditDays=180`, `Retention__EventDays=30`, `Retention__SessionDays=30`.

Behind a reverse proxy, explicitly set `Proxy__KnownProxies__0` to its IP; forwarded headers from other sources are not trusted. Production API requests require HTTPS (including trusted forwarded HTTPS). Persist the Data Protection directory across restarts. Rate limits are per API process: login 10/minute/IP (`RateLimiting__LoginPerMinute`), global 600/minute/IP (`RateLimiting__GlobalPerMinute`), activation 30/minute/IP/application, heartbeat 120/minute/session. Metrics are published by the `KeyValidation.Api` .NET Meter: `api.request.duration`, `licensing.activations`, `licensing.heartbeat.rejections`.

Run `dotnet run --project backend/src/KeyValidation.Api -- migrate` during deployment, separately from API startup. Create account: `dotnet run --project backend/src/KeyValidation.Api -- admin create <userName> <Admin|Operator|ReadOnly>`. Reset: `dotnet run --project backend/src/KeyValidation.Api -- admin reset-password <userName>`. Password comes from `KEYVALIDATION_ADMIN_PASSWORD`, consumed once, or an interactive hidden prompt; never put a password in command arguments. Reset invalidates prior cookies through the security stamp.


## Public developer documentation and MCP

`GET /api/integrations/developer-docs` returns the versioned public catalog index; `GET /api/integrations/developer-docs/{id}` returns `{id,title,topic,markdown,version}` for the selected document (unknown ID: 404). Both are anonymous, read-only and share the MCP catalog; they expose explicitly maintained public developer material only.

`/mcp` is an anonymous stateless Streamable HTTP endpoint provided by ModelContextProtocol.AspNetCore 2.2.0. HTTP paths and protocol headers are preserved through Vite/Nginx, including the routed `/mcp/` form. Send `Accept: application/json, text/event-stream` and JSON-RPC POST bodies. Legacy clients initialize with 2025-11-25 and use `tools/list`, `tools/call`, `resources/list`, `resources/read`. Current 2026-07-28 clients use `server/discover`; each request includes `Mcp-Method` and params `_meta` entries `io.modelcontextprotocol/protocolVersion` and `io.modelcontextprotocol/clientCapabilities` (JSON object). SDK responses may be JSON or SSE containing JSON-RPC messages. MCP sessions are stateless and require no admin cookie, merchant token or CSRF header.

The five read-only tools are `search_integration_docs(query, limit?)`, `get_integration_doc(docId)`, `get_api_reference(operationId)`, `get_code_example(language, scenario)` and `get_troubleshooting_guide(errorCode, operationId?)`. Nine resources use `keygate://docs/{id}`. Invalid/unknown tool inputs produce protocol/tool errors; no-match search returns an empty result and unknown error codes return a general guide. No tool accepts arbitrary URLs, source code, raw logs, credentials or business-operation inputs.

Search query: 1-200 characters; limit: 1-10, default 5; errorCode: 1-100 characters. Example language: csharp/python/javascript; scenario: online-license. Bodies over 64 KiB return 413, including requests without Content-Length. MCP and developer docs default to 60 requests/IP/minute plus the global limit; 429 includes Retry-After. An unlisted Host or supplied Origin returns 403. Missing Origin is permitted. Production `MCP_ALLOWED_HOST`/`MCP_ALLOWED_ORIGIN` must identify the exact public HTTPS site; see README for environment mappings and client configuration. HTTP-level failures retain their status and never become SPA HTML.
