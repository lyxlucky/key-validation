# SDD ledger — plan: docs/superpowers/plans/2026-10-05-account-security-rebinding.md

Status: complete, 2026-10-06. Full suite 133 backend + 17 browser tests passed; production build and model/migration consistency passed.

## Preflight

| Tasks | Shared boundary | Finding |
| --- | --- | --- |
| 1 / 2 | Contracts.cs | Task 1 owns only UserDto; task 2 uses a separate RebindingContracts.cs. |
| 1 / 2 / 4 | EF model | Root adds partial hooks; workers add separate configurations; root generates migration after both finish. |
| 1 / 3 | Security API | Spec fixes paths, login union, UserDto flags and errors. |
| 2 / 3 | Rebinding API | Spec fixes DevicePolicyDto, units and permissions. |
| 1 | First-login tests vs existing tests | Update fixtures/login steps where new account contract intentionally changes. |
| 2 | Legacy compatibility | Migration defaults old cards to unlimited, new Issue uses policy defaults. |
| 3 | Secrets and pending operation state | Secrets memory-only; existing recovery remains account-scoped. |
| 4 | Build/migration/tests | Schema generated from published updated API, not hand-edited. |

Ruling: Proceed with the roadmap's first batch F01/F04/F12 under the recommended defaults while the optional preference question is pending — user's continuation authorizes the work, and existing users/data remain compatible — alternative preferences may require rule adjustments.
Ruling: No Git/worktree workflow in this non-Git workspace; use separate file ownership and persistent reports — matches existing workspace — cannot produce commit-based review ranges.

Ruling: Root implements the frontend after both new-agent spawn and prior-agent follow-up were rejected by the thread limit — backend workers continued independent tasks and the account worker independently reviewed security frontend sources — review evidence uses files and tests rather than a dedicated frontend implementer/reviewer pair.

Task 1: complete. Account security report records 45 targeted tests and security frontend review. Fixes: queued stale credentials, CLI non-admin first-change requirement, expired MFA proof during mandatory password change.
Task 2: complete. Rebinding report records 23 PostgreSQL HTTP/migration tests and 6 domain tests; independent backend review clean.
Task 3: complete. Root implemented auth/security routing, MFA and recovery dialogs, account flags, device policy panel, generated DTO references, styles and error mapping. Security fields stay in component memory and clear on unmount. Account worker read-only review found no remaining blockers.
Task 4: complete. Combined migration applied, API published locally, schema generated, full tests passed, screenshots inspected, API/README/roadmap/verification updated. Original operation and issuance recovery regression tests remain passing.

Final commands: `dotnet test backend/KeyValidation.slnx -c Release --no-restore` (29 + 104); `npm --prefix frontend run test:e2e` (17); `npm --prefix frontend run build` (12.44s Vite); EF pending-model check (none). No client-baseline rerun or new dependency audit claimed.

User confirmation received before delivery: “采用推荐规则，先完成这一批”. The default-rule/scope question is resolved; no rule changes needed. Remaining 19 roadmap items stay deferred.
