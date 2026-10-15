import { test, expect } from '@playwright/test';
import type { Request } from '@playwright/test';
import { login } from './roadmap-helpers';

test.use({ trace: 'off', screenshot: 'off' });
test.afterEach(async ({ page }) => { await page.goto('about:blank'); });

for (const operation of ['api', 'csrf'] as const) {
  test(`an obsolete ${operation} response releases its actual HTTP request`, async ({ page }) => {
    await login(page);
    const completed = new Set<Request>();
    page.on('requestfinished', request => completed.add(request));
    page.on('requestfailed', request => completed.add(request));
    const path = operation === 'api' ? '/api/admin/auth/me' : '/api/admin/auth/csrf';
    await page.evaluate(path => {
      const nativeFetch = window.fetch.bind(window);
      const gate = { held: false, release: () => {}, outcome: 'pending' };
      Object.assign(window, { reviewResponseGate: gate });
      let armed = true;
      window.fetch = async (input, init) => {
        const response = await nativeFetch(input, init);
        const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
        if (armed && new URL(url, location.href).pathname === path) {
          armed = false;
          gate.held = true;
          await new Promise<void>(resolve => { gate.release = resolve; });
        }
        return response;
      };
    }, path);
    const requested = page.waitForRequest(request => new URL(request.url()).pathname === path);
    await page.evaluate(async operation => {
      const moduleUrl = performance.getEntriesByType('resource').map(entry => entry.name).find(url => new URL(url).pathname === '/src/lib/api.ts');
      if (!moduleUrl) throw new Error('The active API module was not loaded');
      const api = await import(moduleUrl);
      const gate = (window as unknown as { reviewResponseGate: { outcome: string } }).reviewResponseGate;
      void (operation === 'api' ? api.api('/admin/auth/me') : api.refreshCsrf()).then(
        () => { gate.outcome = 'resolved'; },
        (error: Error) => { gate.outcome = error.name; },
      );
    }, operation);
    const request = await requested;
    await expect.poll(() => page.evaluate(() => (window as unknown as { reviewResponseGate: { held: boolean } }).reviewResponseGate.held)).toBe(true);
    await page.evaluate(async () => {
      const moduleUrl = performance.getEntriesByType('resource').map(entry => entry.name).find(url => new URL(url).pathname === '/src/lib/api.ts');
      if (!moduleUrl) throw new Error('The active API module was not loaded');
      const api = await import(moduleUrl);
      api.beginAdminSession();
      (window as unknown as { reviewResponseGate: { release: () => void } }).reviewResponseGate.release();
    });
    await expect.poll(() => page.evaluate(() => (window as unknown as { reviewResponseGate: { outcome: string } }).reviewResponseGate.outcome)).toBe('AbortError');
    // A fulfilled route bypasses this browser network lifetime, so use the
    // regression account's read-only HTTP endpoints and assert real completion.
    await expect.poll(() => completed.has(request), { timeout: 5000 }).toBe(true);
    await expect(page.locator('.account-name')).toHaveCount(1);
  });
}

test('a delayed administrator response cannot restore the login cookie after logout', async ({ page }) => {
  await login(page);
  let release = () => {};
  let heldReady = () => {};
  let releaseDone = () => {};
  let responseRenewsCookie = false;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const held = new Promise<void>(resolve => { heldReady = resolve; });
  const delivered = new Promise<void>(resolve => { releaseDone = resolve; });
  await page.route('**/api/admin/dashboard?logoutCookieProbe=1', async route => {
    const response = await route.fetch();
    responseRenewsCookie = response.headersArray().some(header => header.name.toLowerCase() === 'set-cookie' && header.value.startsWith('KeyValidation.Admin=') && !header.value.startsWith('KeyValidation.Admin=;'));
    heldReady();
    await gate;
    try { await route.fulfill({ response }); }
    catch (error) { if (!route.request().failure()) throw error; }
    finally { releaseDone(); }
  });
  await page.evaluate(async () => {
    const moduleUrl = performance.getEntriesByType('resource').map(entry => entry.name).find(url => new URL(url).pathname === '/src/lib/api.ts');
    if (!moduleUrl) throw new Error('The active API module was not loaded');
    const api = await import(moduleUrl);
    const result = { outcome: 'pending' };
    Object.assign(window, { reviewCookieResponse: result });
    void api.api('/admin/dashboard?logoutCookieProbe=1').then(
      () => { result.outcome = 'resolved'; },
      (error: Error) => { result.outcome = error.name; },
    );
  });
  try {
    await held;
    expect(responseRenewsCookie).toBe(true);
    const logoutResponse = page.waitForResponse(response => new URL(response.url()).pathname === '/api/admin/auth/logout');
    await page.getByRole('button', { name: '退出登录', exact: true }).click();
    const loggedOut = await logoutResponse;
    expect(loggedOut.status()).toBe(204);
    const logoutClearsCookie = (await loggedOut.headersArray()).some(header => header.name.toLowerCase() === 'set-cookie' && header.value.startsWith('KeyValidation.Admin=;'));
    expect(logoutClearsCookie).toBe(true);
    await expect(page.getByLabel('用户名', { exact: true })).toBeVisible();
    const cookieAfterLogout = (await page.context().cookies()).some(cookie => cookie.name === 'KeyValidation.Admin');
    expect(cookieAfterLogout).toBe(false);
    release();
    await delivered;
    await expect.poll(() => page.evaluate(() => (window as unknown as { reviewCookieResponse: { outcome: string } }).reviewCookieResponse.outcome)).toBe('AbortError');
    const cookieAfterOldResponse = (await page.context().cookies()).some(cookie => cookie.name === 'KeyValidation.Admin');
    console.info(JSON.stringify({ probe: 'logout-cookie-order', responseRenewsCookie, logoutClearsCookie, cookieAfterLogout, cookieAfterOldResponse }));
    expect(cookieAfterOldResponse).toBe(false);
    await page.reload();
    await expect(page.getByLabel('用户名', { exact: true })).toBeVisible();
    expect((await page.request.get('/api/admin/auth/me')).status()).toBe(401);
  } finally { release(); }
});
