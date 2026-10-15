import { test, expect } from '@playwright/test';
import type { Page, Request, Route } from '@playwright/test';
import { deferred, enter, holdNext, loginAs, renderSettled, syntheticAdmin, visitSystem } from './review-lifecycle-helpers';

test.use({ trace: 'off', screenshot: 'off', serviceWorkers: 'block' });

const cookieName = 'KeyValidation.Admin';
const renewCookie = `${cookieName}=synthetic-late-renewal; Path=/; HttpOnly; SameSite=Lax`;
const hasCookie = async (page: Page) => (await page.context().cookies()).some(cookie => cookie.name === cookieName);

async function json(route: Route, body: unknown, status = 200, cookie?: string) {
  await route.fulfill({ status, contentType: 'application/json', body: status === 204 ? '' : JSON.stringify(body), headers: cookie ? { 'set-cookie': cookie } : {} });
}

async function logoutFixture(page: Page, logoutStatus = 204) {
  const fixture = await syntheticAdmin(page, { mfa: false });
  await page.context().addCookies([{ name: cookieName, value: 'synthetic-review-a', url: 'http://127.0.0.1:5173', httpOnly: true, sameSite: 'Lax' }]);
  const logoutReady = deferred<Request>();
  const logoutGate = deferred();
  const lateGate = deferred();
  const late: { request: Request; settled: Promise<void> }[] = [];
  const state = { phase: 'active', csrfExchanges: 0, logoutExchanges: 0 };
  const paged = { items: [], total: 0, page: 1, pageSize: 20 };
  await page.route('**/api/admin/**', async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === '/api/admin/auth/logout' && state.logoutExchanges === 0) {
      state.logoutExchanges++;
      state.phase = 'pending';
      logoutReady.resolve(request);
      await logoutGate.promise;
      if (request.failure()) return;
      if (logoutStatus === 204) fixture.state.identity = null;
      state.phase = logoutStatus === 204 ? 'transition' : 'failed';
      return json(route, { code: 'REQUEST_FAILED' }, logoutStatus, logoutStatus === 204 ? `${cookieName}=; Path=/; Max-Age=0; HttpOnly; SameSite=Lax` : undefined);
    }
    if (state.phase === 'pending' || state.phase === 'transition') {
      const settled = deferred();
      const finished = (candidate: Request) => {
        if (candidate !== request) return;
        page.off('requestfinished', finished); page.off('requestfailed', finished);
        settled.resolve();
      };
      page.on('requestfinished', finished); page.on('requestfailed', finished);
      late.push({ request, settled: settled.promise });
      await lateGate.promise;
      if (request.failure()) return;
      const body = path === '/api/admin/auth/csrf' ? { token: 'synthetic-late-csrf' }
        : path === '/api/admin/auth/me' ? fixture.user('review-a')
          : path === '/api/admin/auth/access' ? fixture.access() : paged;
      return json(route, body, 200, renewCookie);
    }
    if (path === '/api/admin/auth/me') {
      const authenticated = request.headers().cookie?.includes(`${cookieName}=`) ?? false;
      return json(route, authenticated ? fixture.user(fixture.state.identity ?? 'review-a') : { code: 'UNAUTHORIZED' }, authenticated ? 200 : 401);
    }
    if (path === '/api/admin/auth/csrf') state.csrfExchanges++;
    if (path === '/api/admin/auth/login') {
      fixture.state.identity = request.postDataJSON().userName;
      return json(route, fixture.user(), 200, `${cookieName}=synthetic-review-b; Path=/; HttpOnly; SameSite=Lax`);
    }
    return route.fallback();
  });
  return {
    ...fixture, state, late, logoutReady: logoutReady.promise,
    releaseLogout: () => logoutGate.resolve(),
    releaseLate: async () => { lateGate.resolve(); await Promise.all(late.map(value => value.settled)); },
    cleanup: () => { logoutGate.resolve(); lateGate.resolve(); },
  };
}

async function delayLogoutTransition(page: Page) {
  await page.addInitScript(() => {
    const start = document.startViewTransition.bind(document);
    const gate = { pending: false, release: () => {}, done: false };
    let armed = true;
    Object.assign(window, { logoutBoundaryTransition: gate });
    document.startViewTransition = ((update: () => void | Promise<void>) => start(async () => {
      if (!armed) { await update(); return; }
      armed = false;
      gate.pending = true;
      await new Promise<void>(resolve => { gate.release = resolve; });
      await update();
      gate.done = true;
    })) as typeof document.startViewTransition;
  });
}

async function ordinaryAttempts(page: Page, key: string) {
  await page.evaluate(async key => {
    const modulePath = performance.getEntriesByType('resource').findLast(entry => new URL(entry.name).pathname === '/src/lib/api.ts')?.name;
    if (!modulePath) throw new Error('The application API module was not loaded');
    const { api, refreshCsrf } = await import(/* @vite-ignore */ modulePath);
    const outcomes: Record<string, string> = {};
    const store = window as unknown as { logoutBoundaryAttempts: Record<string, Record<string, string>> };
    store.logoutBoundaryAttempts ??= {};
    store.logoutBoundaryAttempts[key] = outcomes;
    const operations = {
      query: () => api('/admin/accounts?logoutBoundaryProbe=1'),
      mutation: () => api('/admin/accounts', { method: 'POST', body: { userName: 'synthetic-blocked-write' } }),
      me: () => api('/admin/auth/me'),
      access: () => api('/admin/auth/access'),
      csrfApi: () => api('/admin/auth/csrf'),
      csrfRefresh: () => refreshCsrf(),
    };
    for (const [name, operation] of Object.entries(operations)) {
      outcomes[name] = 'pending';
      void operation().then(() => { outcomes[name] = 'resolved'; }, (error: Error) => { outcomes[name] = error.name; });
    }
  }, key);
  await renderSettled(page);
}

async function attemptResults(page: Page, key: string) {
  return page.evaluate(key => (window as unknown as { logoutBoundaryAttempts: Record<string, Record<string, string>> }).logoutBoundaryAttempts[key], key);
}

const blockedAttempts = { query: 'AbortError', mutation: 'AbortError', me: 'AbortError', access: 'AbortError', csrfApi: 'AbortError', csrfRefresh: 'AbortError' };

for (const motion of ['native', 'delayed', 'fallback', 'reduced'] as const) {
  test(`logout admits no retiring UI work or renewing cookies through ${motion} transition`, async ({ page }) => {
    await page.emulateMedia({ reducedMotion: motion === 'reduced' ? 'reduce' : 'no-preference' });
    if (motion === 'delayed') await delayLogoutTransition(page);
    if (motion === 'fallback') await page.addInitScript(() => { Object.defineProperty(document, 'startViewTransition', { value: undefined }); });
    const fixture = await logoutFixture(page);
    try {
      await enter(page);
      await expect(page.locator('.account-name strong')).toHaveText('review-a');
      expect(await hasCookie(page)).toBe(true);
      await page.getByRole('button', { name: '退出登录', exact: true }).click();
      const logoutRequest = await fixture.logoutReady;
      expect(logoutRequest.method()).toBe('POST');
      expect(logoutRequest.headers()['x-csrf-token'] === 'csrf-review-a').toBe(true);
      // The actual second UI click starts the previously missing Accounts GET.
      await page.getByRole('menuitem', { name: '账号管理', exact: true }).click();
      await expect(page.getByRole('heading', { name: '账号管理', exact: true })).toBeVisible();
      await ordinaryAttempts(page, 'pending');
      const duringLogout = fixture.late.length;
      const accountsFromUi = fixture.late.filter(value => new URL(value.request.url()).pathname === '/api/admin/accounts' && value.request.method() === 'GET' && !value.request.url().includes('logoutBoundaryProbe')).length;
      const response = page.waitForResponse(response => new URL(response.url()).pathname === '/api/admin/auth/logout');
      fixture.releaseLogout();
      expect((await response).status()).toBe(204);
      if (motion === 'delayed') {
        await expect.poll(() => page.evaluate(() => (window as unknown as { logoutBoundaryTransition: { pending: boolean } }).logoutBoundaryTransition.pending)).toBe(true);
        // Keep the old shell mounted even after the browser accepted logout204.
        await page.locator('.panel-heading button').evaluate(element => (element as HTMLButtonElement).click());
      } else await expect(page.getByLabel('用户名', { exact: true })).toBeVisible();
      const cookieAfterLogout = await hasCookie(page);
      await ordinaryAttempts(page, 'after204');
      await fixture.releaseLate();
      await expect.poll(async () => Object.values(await attemptResults(page, 'after204')).includes('pending')).toBe(false);
      const cookieAfterLate = await hasCookie(page);
      const uncancelled = fixture.late.filter(value => !value.request.failure()).length;
      if (motion === 'delayed') await page.evaluate(() => (window as unknown as { logoutBoundaryTransition: { release: () => void } }).logoutBoundaryTransition.release());
      await expect(page.getByLabel('用户名', { exact: true })).toBeVisible();
      const cookieAfterLoginScreen = await hasCookie(page);
      console.info(JSON.stringify({ probe: 'logout-admission', motion, accountsFromUi, duringLogout, after204: fixture.late.length - duringLogout, uncancelled, cookieAfterLogout, cookieAfterLate, cookieAfterLoginScreen, realApiRequests: 0 }));
      expect.soft(duringLogout).toBe(0);
      expect.soft(fixture.late.length).toBe(0);
      expect.soft(await attemptResults(page, 'pending')).toEqual(blockedAttempts);
      expect.soft(await attemptResults(page, 'after204')).toEqual(blockedAttempts);
      expect.soft(cookieAfterLogout).toBe(false);
      expect.soft(cookieAfterLate).toBe(false);
      expect.soft(cookieAfterLoginScreen).toBe(false);
      expect(fixture.state.logoutExchanges).toBe(1);
      expect(fixture.state.csrfExchanges).toBe(1);
      fixture.state.phase = 'complete';
      await page.reload();
      await expect.soft(page.getByLabel('用户名', { exact: true })).toBeVisible();
      expect.soft(await hasCookie(page)).toBe(false);
    } finally {
      fixture.cleanup();
      await page.evaluate(() => (window as unknown as { logoutBoundaryTransition?: { release: () => void } }).logoutBoundaryTransition?.release()).catch(() => {});
    }
  });
}

test('a failed logout restores usable current work and allows a later logout and login', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const fixture = await logoutFixture(page, 500);
  try {
    await enter(page);
    await page.getByRole('button', { name: '退出登录', exact: true }).click();
    await fixture.logoutReady;
    await ordinaryAttempts(page, 'pending');
    fixture.releaseLogout();
    await expect(page.locator('.ant-message-error')).toBeVisible();
    await fixture.releaseLate();
    expect(fixture.late.length).toBe(0);
    expect(await attemptResults(page, 'pending')).toEqual(blockedAttempts);
    await expect(page.locator('.account-name strong')).toHaveText('review-a');
    expect(await hasCookie(page)).toBe(true);
    await page.getByRole('menuitem', { name: '账号管理', exact: true }).click();
    await expect(page.getByText('review-a-account', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: /刷新/ }).click();
    await expect(page.getByText('review-a-account', { exact: true })).toBeVisible();
    await page.route('**/api/admin/auth/logout', route => json(route, null, 204, `${cookieName}=; Path=/; Max-Age=0; HttpOnly; SameSite=Lax`));
    await page.getByRole('button', { name: '退出登录', exact: true }).click();
    await expect(page.getByLabel('用户名', { exact: true })).toBeVisible();
    expect(await hasCookie(page)).toBe(false);
    fixture.state.phase = 'complete';
    await loginAs(page);
    await expect(page.locator('.account-name strong')).toHaveText('review-b');
    expect(await hasCookie(page)).toBe(true);
  } finally { fixture.cleanup(); }
});

test('logout stops ordinary work while its private CSRF exchange is still pending', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const fixture = await logoutFixture(page);
  let releaseCsrf = async () => {};
  try {
    await enter(page);
    const csrf = await holdNext(page, '/admin/auth/csrf', { token: 'csrf-review-a' });
    releaseCsrf = csrf.release;
    await page.getByRole('button', { name: '退出登录', exact: true }).click();
    await csrf.held;
    const admitted: string[] = [];
    page.on('request', request => { if (new URL(request.url()).pathname.startsWith('/api/admin/')) admitted.push(new URL(request.url()).pathname); });
    await page.getByRole('menuitem', { name: '账号管理', exact: true }).click();
    await expect(page.getByRole('heading', { name: '账号管理', exact: true })).toBeVisible();
    await ordinaryAttempts(page, 'csrfPending');
    await expect.poll(async () => Object.values(await attemptResults(page, 'csrfPending')).includes('pending')).toBe(false);
    expect.soft(await attemptResults(page, 'csrfPending')).toEqual(blockedAttempts);
    expect.soft(admitted.length).toBe(0);
    await csrf.release();
    await fixture.logoutReady;
    fixture.releaseLogout();
    await expect(page.getByLabel('用户名', { exact: true })).toBeVisible();
    expect(await hasCookie(page)).toBe(false);
  } finally { fixture.cleanup(); await releaseCsrf(); }
});

test('an explicit new login admits its authentication exchange without reopening ordinary work early', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await delayLogoutTransition(page);
  const fixture = await logoutFixture(page);
  const loginGate = deferred();
  const loginReady = deferred();
  try {
    await enter(page);
    await page.getByRole('button', { name: '退出登录', exact: true }).click();
    await fixture.logoutReady;
    fixture.releaseLogout();
    await expect(page.getByLabel('用户名', { exact: true })).toBeVisible();
    fixture.state.phase = 'complete';
    await page.emulateMedia({ reducedMotion: 'no-preference' });
    await page.route('**/api/admin/auth/login', async route => {
      loginReady.resolve();
      await loginGate.promise;
      return route.fallback();
    });
    await page.getByLabel('用户名', { exact: true }).fill('review-b');
    await page.getByLabel('密码', { exact: true }).fill('Synthetic-review-password!1');
    await page.getByRole('button', { name: '登录', exact: true }).click();
    await loginReady.promise;
    const admitted: string[] = [];
    page.on('request', request => { if (new URL(request.url()).pathname.startsWith('/api/admin/')) admitted.push(new URL(request.url()).pathname); });
    await ordinaryAttempts(page, 'loginPending');
    await expect.poll(async () => Object.values(await attemptResults(page, 'loginPending')).includes('pending')).toBe(false);
    expect.soft(await attemptResults(page, 'loginPending')).toEqual(blockedAttempts);
    expect.soft(admitted.length).toBe(0);
    loginGate.resolve();
    await expect.poll(() => page.evaluate(() => (window as unknown as { logoutBoundaryTransition: { pending: boolean } }).logoutBoundaryTransition.pending)).toBe(true);
    admitted.length = 0;
    await ordinaryAttempts(page, 'loginTransition');
    expect(await attemptResults(page, 'loginTransition')).toEqual(blockedAttempts);
    expect(admitted.length).toBe(0);
    await page.evaluate(() => (window as unknown as { logoutBoundaryTransition: { release: () => void } }).logoutBoundaryTransition.release());
    await expect(page.locator('.account-name strong')).toHaveText('review-b');
    await visitSystem(page, '账号管理');
    await expect(page.getByText('review-b-account', { exact: true })).toBeVisible();
    expect(await hasCookie(page)).toBe(true);
  } finally {
    fixture.cleanup(); loginGate.resolve();
    await page.evaluate(() => (window as unknown as { logoutBoundaryTransition?: { release: () => void } }).logoutBoundaryTransition?.release()).catch(() => {});
  }
});

for (const window of ['http', 'transition'] as const) {
  test(`a stale logout ${window} completion cannot clear a newer login or its cookie`, async ({ page }) => {
    await page.emulateMedia({ reducedMotion: window === 'transition' ? 'no-preference' : 'reduce' });
    if (window === 'transition') await delayLogoutTransition(page);
    const fixture = await logoutFixture(page);
    try {
      await enter(page);
      await page.getByRole('button', { name: '退出登录', exact: true }).click();
      const request = await fixture.logoutReady;
      if (window === 'transition') {
        fixture.releaseLogout();
        await expect.poll(() => page.evaluate(() => (globalThis as unknown as { logoutBoundaryTransition: { pending: boolean } }).logoutBoundaryTransition.pending)).toBe(true);
      }
      // Deliver the same ownership-tagged expiry signal used by the transport.
      // It creates a replacement login form without releasing the old callback.
      await page.evaluate(async () => {
        const modulePath = performance.getEntriesByType('resource').findLast(entry => new URL(entry.name).pathname === '/src/lib/api.ts')?.name;
        if (!modulePath) throw new Error('The application API module was not loaded');
        const { getAdminSessionGeneration } = await import(/* @vite-ignore */ modulePath);
        globalThis.dispatchEvent(new CustomEvent('session-expired', { detail: getAdminSessionGeneration() }));
      });
      fixture.state.phase = 'complete';
      await expect(page.getByLabel('用户名', { exact: true })).toBeVisible();
      await loginAs(page);
      if (window === 'http') {
        await expect.poll(() => request.failure() !== null).toBe(true);
        fixture.releaseLogout();
      } else {
        await page.evaluate(() => (globalThis as unknown as { logoutBoundaryTransition: { release: () => void } }).logoutBoundaryTransition.release());
        await expect.poll(() => page.evaluate(() => (globalThis as unknown as { logoutBoundaryTransition: { done: boolean } }).logoutBoundaryTransition.done)).toBe(true);
      }
      await renderSettled(page);
      await expect(page.locator('.account-name strong')).toHaveText('review-b');
      expect(await hasCookie(page)).toBe(true);
      await page.reload();
      await expect(page.locator('.account-name strong')).toHaveText('review-b');
    } finally {
      fixture.cleanup();
      await page.evaluate(() => (globalThis as unknown as { logoutBoundaryTransition?: { release: () => void } }).logoutBoundaryTransition?.release()).catch(() => {});
    }
  });
}
