import { expect } from '@playwright/test';
import type { Locator, Page, Request, Route } from '@playwright/test';

export const syntheticPassword = 'Synthetic-review-password!1';
export const syntheticCode = 'synthetic-review-code';
export const syntheticKey = 'SYNTHETIC-REVIEW-ISSUED-KEY';
export const syntheticSharedKey = 'SYNTHETICREVIEWKEY';
export const roleOne = '11111111-1111-4111-8111-111111111111';
export const roleTwo = '22222222-2222-4222-8222-222222222222';

export function deferred<T = void>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => { resolve = done; });
  return { promise, resolve };
}

export async function fulfill(route: Route, body: unknown, status = 200) {
  await route.fulfill({ status, contentType: 'application/json', body: status === 204 ? '' : JSON.stringify(body) });
}

// Every API request is fulfilled locally. These fixtures exercise the running
// application without issuing credentials or changing any real account.
export async function syntheticAdmin(page: Page, options: { mfa?: boolean; signedIn?: boolean; permissions?: string[] } = {}) {
  const state = {
    identity: options.signedIn === false ? null : 'review-a' as string | null,
    mfa: options.mfa ?? true,
    permissions: options.permissions ?? ['platform.manage'],
    mustChangePassword: false,
    challenge: false,
    requests: [] as { path: string; method: string; csrf: string | undefined }[],
  };
  const user = (identity = state.identity) => ({ id: identity, userName: identity, roles: ['ReviewPlatform'], mustChangePassword: state.mustChangePassword, twoFactorEnabled: state.mfa });
  const access = () => ({ permissions: [...state.permissions], allApplications: true, applicationIds: [], channelId: null });
  const account = () => ({ id: 'review-account', userName: `${state.identity}-account`, roles: ['Review role one'], roleIds: [roleOne], createdAt: '2026-10-01T00:00:00Z', disabledAt: null, mustChangePassword: false, twoFactorEnabled: false });
  const customer = { id: 'review-customer', applicationId: 'review-application', channelId: null, name: 'Review customer', email: null, phone: null, note: null, disabledAt: null, anonymizedAt: null, createdAt: '2026-10-01T00:00:00Z' };
  const application = { id: 'review-application', name: 'Review application', code: 'review-application', disabledAt: null, createdAt: '2026-10-01T00:00:00Z' };
  const plan = { id: 'review-plan', applicationId: application.id, name: 'Review plan', durationSeconds: 86400, maxDevices: 1, maxConcurrentSessions: 1, isEnabled: true, createdAt: '2026-10-01T00:00:00Z' };
  const paged = (items: unknown[]) => ({ items, total: items.length, page: 1, pageSize: 100 });
  await page.context().route('**/api/**', async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const method = request.method();
    state.requests.push({ path, method, csrf: request.headers()['x-csrf-token'] });
    if (path === '/api/admin/auth/csrf') return fulfill(route, { token: `csrf-${state.identity ?? 'anonymous'}` });
    if (path === '/api/admin/auth/login') {
      state.identity = request.postDataJSON().userName;
      return fulfill(route, state.challenge ? { requiresTwoFactor: true, challengeToken: 'synthetic-challenge' } : user());
    }
    if (path === '/api/admin/auth/two-factor') return fulfill(route, user());
    if (path === '/api/admin/auth/logout') { state.identity = null; return fulfill(route, null, 204); }
    if (path === '/api/admin/auth/me') return fulfill(route, state.identity ? user() : { code: 'UNAUTHORIZED' }, state.identity ? 200 : 401);
    if (path === '/api/admin/auth/access') return fulfill(route, access());
    if (path === '/api/admin/review-expire') return fulfill(route, { code: 'UNAUTHORIZED' }, 401);
    if (path === '/api/admin/security') return fulfill(route, { mustChangePassword: state.mustChangePassword, twoFactorEnabled: state.mfa, recoveryCodesRemaining: state.mfa ? 10 : 0, verificationExpiresAt: null });
    if (path === '/api/admin/security/two-factor/setup') return fulfill(route, { sharedKey: syntheticSharedKey, authenticatorUri: `otpauth://totp/Review?secret=${syntheticSharedKey}&issuer=Review` });
    if (path === '/api/admin/security/two-factor/confirm' || path === '/api/admin/security/two-factor/recovery-codes') { state.mfa = true; return fulfill(route, { recoveryCodes: [syntheticCode] }); }
    if (path === '/api/admin/security/two-factor/disable') { state.mfa = false; return fulfill(route, null, 204); }
    if (path === '/api/admin/security/password') { state.mustChangePassword = false; return fulfill(route, null, 204); }
    if (path === '/api/admin/security/verify') return fulfill(route, null, 204);
    if (path === '/api/admin/accounts') return fulfill(route, method === 'GET' ? paged([account()]) : account(), method === 'GET' ? 200 : 201);
    if (path === '/api/admin/roles') return fulfill(route, paged([
      { id: roleOne, name: 'Review role one', description: null, permissions: ['licenses.read'], isBuiltIn: false, userCount: 1 },
      { id: roleTwo, name: 'Review role two', description: null, permissions: ['reports.read'], isBuiltIn: false, userCount: 0 },
    ]));
    if (path === '/api/admin/catalog') return fulfill(route, { applications: [application], accounts: [account()] });
    if (path === '/api/admin/customers') return fulfill(route, paged([customer]));
    if (path === '/api/admin/customers/review-customer/rotate-access-code') return fulfill(route, { accessCode: syntheticCode });
    if (path === '/api/admin/applications') return fulfill(route, paged([application]));
    if (path === `/api/admin/applications/${application.id}/plans`) return fulfill(route, paged([plan]));
    if (path === '/api/admin/license-batches' && method === 'POST') return fulfill(route, { batch: { id: 'review-batch', applicationId: application.id, planId: plan.id, quantity: 1 }, licenses: [{ id: 'review-license', key: syntheticKey, keyHint: 'review-key-hint' }] }, 201);
    return fulfill(route, paged([]));
  });
  return { state, user, access };
}

export async function renderSettled(page: Page) {
  await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
}

export async function holdNext(page: Page, path: string, body: unknown, status = 200) {
  const held = deferred<Request>();
  const gate = deferred();
  const settled = deferred();
  let used = false;
  await page.route(`**/api${path}`, async route => {
    if (used) return route.fallback();
    used = true;
    const request = route.request();
    const finished = (candidate: Request) => {
      if (candidate !== request) return;
      page.off('requestfinished', finished); page.off('requestfailed', finished);
      settled.resolve();
    };
    page.on('requestfinished', finished); page.on('requestfailed', finished);
    held.resolve(request);
    await gate.promise;
    if (request.failure()) return;
    await fulfill(route, body, status);
  });
  return {
    held: held.promise,
    release: async () => {
      await held.promise;
      gate.resolve();
      await settled.promise;
      await renderSettled(page);
    },
  };
}

export async function enter(page: Page, path = '/security') {
  await page.goto(path);
  await expect(page.locator('#main-content')).toBeVisible();
}

export async function visitSystem(page: Page, name: string) {
  const item = page.getByRole('menuitem', { name, exact: true });
  const group = page.getByRole('menuitem', { name: '系统管理', exact: true });
  // A closing accordion item can still be painted during its exit animation.
  // Read the group's open state before deciding whether to expand it.
  if (await group.getAttribute('aria-expanded') !== 'true') await group.click();
  await expect(group).toHaveAttribute('aria-expanded', 'true');
  await item.click();
}

export async function hiddenPanel(page: Page, label: string) {
  const panelId = await page.getByRole('tab', { name: label, exact: true }).getAttribute('aria-controls');
  await expect(page.locator(`#${panelId}`)).toHaveAttribute('hidden', '');
  await renderSettled(page);
}

export async function loginAs(page: Page, name = 'review-b') {
  await page.locator('#admin-login_userName').fill(name);
  await page.locator('#admin-login_password').fill(syntheticPassword);
  await page.locator('.login-form-card button[type=submit]').click();
  await expect(page.locator('.account-name strong')).toHaveText(name);
}

export async function logoutAndLogin(page: Page, name = 'review-b') {
  await page.getByRole('button', { name: '退出登录', exact: true }).click();
  await expect(page).toHaveURL(/\/login$/);
  await loginAs(page, name);
}

export async function openPasswordEditor(page: Page) {
  await page.getByRole('button', { name: '修改密码', exact: true }).click();
  await page.locator('#security-password_currentPassword').fill(syntheticPassword);
  await page.locator('#security-password_newPassword').fill(`${syntheticPassword}New`);
  await page.locator('#security-password_confirmPassword').fill(`${syntheticPassword}New`);
}

export async function openCreateAccount(page: Page) {
  await page.getByRole('button', { name: '创建账号', exact: true }).click();
  await page.locator('#account-editor_userName').fill('review-retained-draft');
  await page.locator('#account-editor_password').fill(syntheticPassword);
  await page.locator('#account-editor_roleIds').click();
  await page.getByTitle('Review role two', { exact: true }).click();
  await page.locator('#account-editor_roleIds').press('Escape');
}

export async function closeAndInspect(button: Locator, selector: string, marker: string) {
  // Inspect after React's discrete-event commit, before the exit motion finishes.
  return button.evaluate(async (element, values) => {
    const shell = element.closest('.ant-modal');
    (element as HTMLButtonElement).click();
    await Promise.resolve();
    return {
      nodes: document.querySelectorAll(values.selector).length,
      plaintext: document.body.textContent?.includes(values.marker) ?? false,
      shellRetained: shell?.isConnected ?? false,
    };
  }, { selector, marker });
}

export async function expireCurrentSession(page: Page) {
  await page.evaluate(async () => {
    // Vite may add an HMR timestamp. Import that exact module instance so this
    // probe goes through the application's current session owner.
    const modulePath = performance.getEntriesByType('resource').findLast(entry => new URL(entry.name).pathname === '/src/lib/api.ts')?.name;
    if (!modulePath) throw new Error('The application API module was not loaded');
    const { getAdminSessionGeneration } = await import(/* @vite-ignore */ modulePath);
    // Exercise the provider's expiry boundary while authentication is pending;
    // business requests are intentionally not admitted during that phase.
    window.dispatchEvent(new CustomEvent('session-expired', { detail: getAdminSessionGeneration() }));
  });
}

export async function primeCsrf(page: Page) {
  await page.evaluate(async () => {
    const modulePath = performance.getEntriesByType('resource').findLast(entry => new URL(entry.name).pathname === '/src/lib/api.ts')?.name;
    if (!modulePath) throw new Error('The application API module was not loaded');
    const { refreshCsrf } = await import(/* @vite-ignore */ modulePath);
    await refreshCsrf();
  });
}
