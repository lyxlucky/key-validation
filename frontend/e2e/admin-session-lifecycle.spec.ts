import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { enter, expireCurrentSession, holdNext, logoutAndLogin, openCreateAccount, openPasswordEditor, primeCsrf, syntheticAdmin, visitSystem } from './review-lifecycle-helpers';

test.use({ trace: 'off', screenshot: 'off', reducedMotion: 'reduce', serviceWorkers: 'block' });

function invokeOwnedAuthenticationRead(page: Page) {
  return page.evaluate(async () => {
    const modulePath = performance.getEntriesByType('resource').findLast(entry => new URL(entry.name).pathname === '/src/lib/api.ts')?.name;
    if (!modulePath) throw new Error('The application API module was not loaded');
    const { beginAdminAuthentication } = await import(/* @vite-ignore */ modulePath);
    // Anonymous/challenged screens now reject ordinary business requests. Hold
    // an explicitly owned auth read to keep testing cancellation at this boundary.
    const authentication = beginAdminAuthentication();
    try { await authentication.request('/admin/auth/me'); return 'resolved'; }
    catch (error) { return (error as Error).name; }
  });
}

async function enterChallenge(page: Page) {
  const fixture = await syntheticAdmin(page, { signedIn: false });
  fixture.state.challenge = true;
  await page.goto('/login');
  await page.getByLabel('用户名', { exact: true }).fill('review-b');
  await page.getByLabel('密码', { exact: true }).fill('Synthetic-review-password!1');
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByText('完成双重验证', { exact: true })).toBeVisible();
  return fixture;
}

test('provider cleanup cancels superseded bootstrap HTTP requests without a login error', async ({ page }) => {
  await page.addInitScript(() => {
    const nativeFetch = window.fetch.bind(window);
    const outcomes: string[] = [];
    Object.assign(window, { reviewBootstrapRequests: outcomes });
    window.fetch = (input, init) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
      const index = new URL(url, location.href).pathname === '/api/admin/auth/me' ? outcomes.push('pending') - 1 : -1;
      return nativeFetch(input, init).then(response => {
        if (index >= 0) outcomes[index] = String(response.status);
        return response;
      }, error => {
        if (index >= 0) outcomes[index] = error.name;
        throw error;
      });
    };
  });
  await syntheticAdmin(page);
  await enter(page, '/accounts');
  await expect(page.locator('.account-name strong')).toHaveText('review-a');
  await expect.poll(() => page.evaluate(() => (window as unknown as { reviewBootstrapRequests: string[] }).reviewBootstrapRequests.slice(0, 2))).toEqual(['AbortError', '200']);
  await expect(page.locator('.ant-alert-error')).toHaveCount(0);
});

test('starting a password login cancels requests owned by the previous session', async ({ page }) => {
  await syntheticAdmin(page, { signedIn: false });
  await page.goto('/login');
  await expect(page.getByLabel('用户名', { exact: true })).toBeVisible();
  const old = await holdNext(page, '/admin/auth/me', {});
  const pending = invokeOwnedAuthenticationRead(page);
  const request = await old.held;
  try {
    await page.getByLabel('用户名', { exact: true }).fill('review-b');
    await page.getByLabel('密码', { exact: true }).fill('Synthetic-review-password!1');
    await page.getByRole('button', { name: '登录', exact: true }).click();
    await expect(page.locator('.account-name strong')).toHaveText('review-b');
    await expect.poll(() => request.failure() !== null, { timeout: 5000 }).toBe(true);
  } finally { await old.release(); }
  expect(await pending).toBe('AbortError');
});

test('canceling a challenge cancels its pending requests and leaves a clean password form', async ({ page }) => {
  await enterChallenge(page);
  const old = await holdNext(page, '/admin/auth/me', {});
  const pending = invokeOwnedAuthenticationRead(page);
  const request = await old.held;
  try {
    await page.getByRole('button', { name: '返回密码登录', exact: true }).click();
    await expect(page.getByLabel('用户名', { exact: true })).toBeVisible();
    await expect.poll(() => request.failure() !== null, { timeout: 5000 }).toBe(true);
  } finally { await old.release(); }
  expect(await pending).toBe('AbortError');
  await expect(page.locator('.login-form-card .ant-alert-error')).toHaveCount(0);
});

test('an expired in-flight MFA login cannot show an abort error on the replacement form', async ({ page }) => {
  const fixture = await enterChallenge(page);
  const old = await holdNext(page, '/admin/auth/two-factor', fixture.user('review-b'));
  await page.getByLabel('验证码', { exact: true }).fill('123456');
  await page.getByRole('button', { name: '验证并登录', exact: true }).click();
  await old.held;
  await expireCurrentSession(page);
  await old.release();
  await expect(page.getByLabel('用户名', { exact: true })).toBeVisible();
  await expect(page.locator('.login-form-card .ant-alert-error')).toHaveCount(0);
  await expect(page.getByRole('button', { name: '登录', exact: true })).toBeEnabled();
});

for (const failure of [
  { status: 401, code: 'UNAUTHENTICATED' },
  { status: 403, code: 'PASSWORD_CHANGE_REQUIRED' },
]) {
  test(`obsolete ${failure.code} responses cannot disturb a newer login`, async ({ page }) => {
    await syntheticAdmin(page);
    const old = await holdNext(page, '/admin/security', { code: failure.code }, failure.status);
    await enter(page);
    await old.held;
    await logoutAndLogin(page);
    const currentUrl = page.url();
    await old.release();
    await expect(page.locator('.account-name strong')).toHaveText('review-b');
    await expect(page).toHaveURL(currentUrl);
  });

  test(`current ${failure.code} responses still enforce the owning session`, async ({ page }) => {
    await syntheticAdmin(page);
    await enter(page, '/accounts');
    await expect(page.getByText('review-a-account', { exact: true })).toBeVisible();
    const current = await holdNext(page, '/admin/accounts?**', { code: failure.code }, failure.status);
    await page.getByRole('button', { name: /刷新/ }).click();
    await current.release();
    await expect(page).toHaveURL(failure.status === 401 ? /\/login$/ : /\/security$/);
    if (failure.status === 401) await expect(page.getByRole('tablist', { name: '工作区标签', exact: true })).toHaveCount(0);
    else await expect(page.locator('.account-name strong')).toHaveText('review-a');
  });
}

for (const endpoint of ['me', 'access']) {
  test(`an obsolete refresh ${endpoint} response cannot restore the previous account or access`, async ({ page }) => {
    const fixture = await syntheticAdmin(page);
    await enter(page, '/accounts');
    await page.getByRole('menuitem', { name: '账号安全', exact: true }).click();
    const old = await holdNext(page, `/admin/auth/${endpoint}`, endpoint === 'me' ? fixture.user() : fixture.access());
    await openPasswordEditor(page);
    await page.locator('button[form="security-editor-form"]').click();
    await old.held;
    await page.goBack();
    await logoutAndLogin(page);
    await visitSystem(page, '账号管理');
    await expect(page.getByText('review-b-account', { exact: true })).toBeVisible();
    await old.release();
    await expect(page.locator('.account-name strong')).toHaveText('review-b');
    await expect(page.getByText('review-b-account', { exact: true })).toBeVisible();
    await expect(page.getByText('review-a-account', { exact: true })).toHaveCount(0);
  });
}

test('an obsolete CSRF refresh cannot replace the new account token', async ({ page }) => {
  const fixture = await syntheticAdmin(page);
  await enter(page, '/accounts');
  await page.getByRole('menuitem', { name: '账号安全', exact: true }).click();
  // Let the password submission acquire its token; hold refreshUser's next one.
  await openPasswordEditor(page);
  await primeCsrf(page);
  const old = await holdNext(page, '/admin/auth/csrf', { token: 'csrf-obsolete' });
  await page.locator('button[form="security-editor-form"]').click();
  await old.held;
  await page.goBack();
  await logoutAndLogin(page);
  await old.release();
  await visitSystem(page, '账号管理');
  await openCreateAccount(page);
  await page.locator('button[form="account-editor-form"]').click();
  await expect(page.locator('#account-editor-form')).toHaveCount(0);
  const submitted = fixture.state.requests.find(request => request.path === '/api/admin/accounts' && request.method === 'POST');
  expect(submitted?.csrf === 'csrf-review-b').toBe(true);
});

test('a write waiting for old CSRF material is never sent under the next account', async ({ page }) => {
  const fixture = await syntheticAdmin(page);
  await enter(page);
  await page.getByRole('menuitem', { name: '账号管理', exact: true }).click();
  await openCreateAccount(page);
  const old = await holdNext(page, '/admin/auth/csrf', { token: 'csrf-obsolete' });
  await page.locator('button[form="account-editor-form"]').click();
  await old.held;
  await page.goBack();
  await logoutAndLogin(page);
  await old.release();
  expect(fixture.state.requests.filter(request => request.path === '/api/admin/accounts' && request.method === 'POST')).toHaveLength(0);
  await expect(page.locator('.account-name strong')).toHaveText('review-b');
});

test('a queued login screen update cannot revive a session that expired before its callback', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'no-preference' });
  await page.addInitScript(() => {
    const start = document.startViewTransition.bind(document);
    const gate = { pending: false, release: () => {}, done: false };
    Object.assign(window, { reviewTransition: gate });
    document.startViewTransition = ((update: () => void | Promise<void>) => start(async () => {
      gate.pending = true;
      await new Promise<void>(resolve => { gate.release = resolve; });
      await update();
      gate.done = true;
    })) as typeof document.startViewTransition;
  });
  await syntheticAdmin(page, { signedIn: false });
  await page.goto('/login');
  await page.locator('#admin-login_userName').fill('review-b');
  await page.locator('#admin-login_password').fill('Synthetic-review-password!1');
  await page.locator('.login-form-card button[type=submit]').click();
  await expect.poll(() => page.evaluate(() => (window as unknown as { reviewTransition: { pending: boolean } }).reviewTransition.pending)).toBe(true);
  await expireCurrentSession(page);
  await page.evaluate(() => (window as unknown as { reviewTransition: { release: () => void } }).reviewTransition.release());
  await expect.poll(() => page.evaluate(() => (window as unknown as { reviewTransition: { done: boolean } }).reviewTransition.done)).toBe(true);
  await expect(page).toHaveURL(/\/login$/);
  await expect(page.locator('.account-name')).toHaveCount(0);
  await expect(page.getByRole('tablist', { name: '工作区标签', exact: true })).toHaveCount(0);
  await expect(page.locator('.login-form-card .ant-alert-error')).toHaveCount(0);
});
