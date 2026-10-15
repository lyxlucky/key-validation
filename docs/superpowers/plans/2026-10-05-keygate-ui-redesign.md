# KeyGate UI Redesign Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Deliver the approved light administration UI across every page and overlay.

**Architecture:** Retain React feature modules and API hooks. Centralize theme tokens and reusable presentation; make navigation responsive with Ant Design Grid and Drawer.

**Tech Stack:** React 19, TypeScript, Ant Design 6, Vite, Playwright.

**Spec:** `docs/superpowers/specs/2026-10-05-keygate-ui-redesign-design.md`

## Global Constraints

- Preserve APIs, authorization, CSRF and issuance recovery semantics.
- Never persist generated plaintext keys.
- Breakpoints: phone <768px, tablet 768–1023px, desktop ≥1024px.
- Verify widths 375, 768, 1024 and 1440px.
- Use existing dependencies and system fonts.
- Workspace has no Git repository; deliver files without commits.

### Task 1: Theme, navigation and login

Files: `frontend/src/main.tsx`, `frontend/src/App.tsx`, `frontend/src/styles.css`, `frontend/src/components/common.tsx`, `frontend/src/features/auth/LoginPage.tsx`, `frontend/e2e/ui.spec.ts`.

- [x] Add navigation regression: at 375px log in, click the button named 打开导航, choose 应用管理, verify heading and closed drawer; reopen and press Escape, verify focus returns.
  ```ts
  await page.getByRole('button', { name: '打开导航' }).click();
  await page.getByRole('menuitem', { name: '应用管理' }).click();
  await expect(page.getByRole('heading', { name: '应用管理', exact: true })).toBeVisible();
  await expect(page.getByRole('dialog', { name: '工作空间导航' })).toBeHidden();
  ```
- [x] Run this test and confirm it fails because the current UI has no mobile menu button.
- [x] Implement `Brand()` and enhance `PageTitle({title,subtitle,extra})`; render `main#main-content`, a skip link, labeled navigation, responsive Sider and mobile Drawer. Use `Grid.useBreakpoint()` with a 1024px lg theme breakpoint. Close navigation after selecting a route and support Ant Design focus restoration.
- [x] Apply design-system tokens, replace login presentation with dark brand panel and native CSS/icon authorization diagram, preserve form labels and submit behavior.
- [x] Verify navigation test and production compilation.

### Task 2: Management pages and overlays

Files: `OverviewPage.tsx`, `ApplicationsPage.tsx`, `PlansDrawer.tsx`, `LicensesPage.tsx`, `LicenseDrawer.tsx`, `IssueModal.tsx`, `AuditPage.tsx` under their existing feature directories.

- [x] Rebuild overview into four metrics, recent licenses panel and a compact linked workflow panel. Use existing API counts only.
- [x] Give application and audit panels consistent titles, counts and labeled filters. Preserve role-specific actions and server pagination.
- [x] Group license filters and exact-key lookup; add reset by clearing applicationId, status, batchId, batchFilter and resetting page to 1. Preserve exact lookup POST and clearing key after success.
- [x] Style application/plan forms, license detail sections, device cards and issuance steps. Use responsive form grids and internal table scrolling.
- [x] Preserve `hadPendingRequest`, pending storage keys, all issuance error handling and one-time CSV result lifetime without modifying the submit logic.
- [x] Run the existing business E2E suite. Update the obsolete mobile sidebar-width assertion to assert no visible fixed sidebar and an available navigation button.

### Task 3: Responsive review and delivery

Files: `frontend/e2e/ui.spec.ts`, `frontend/e2e/admin.spec.ts`, `docs/verification.md`, the design spec and this plan. Screenshot capture is integrated into the browser tests.

- [x] Add viewport tests for all routes at 375, 768, 1024, 1440px with actual heading visibility and `document.documentElement.scrollWidth <= innerWidth` after fonts and layouts settle.
- [x] Exercise mobile application modal, plan editor, issuance modal, license detail and date filters. Preserve focus and descriptive labels.
- [x] Capture actual login, overview, management pages and overlays; inspect representative desktop and phone images.
- [x] Run `npm --prefix frontend run build` and `npm --prefix frontend run test:e2e` with private local test credentials supplied through environment variables.
- [x] Review changes against the spec and business invariants, address findings, and record exact evidence and preview URL.

## Plan self-review

All five pages and every existing overlay are covered. Navigation is the only substantive behavior addition, with a failing/passing test. Visual presentation uses screenshot review and existing workflow tests instead of implementation-mirroring tests. No new APIs or packages are required.

