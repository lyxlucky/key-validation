import { test, expect } from '@playwright/test';

test.use({ trace: 'off', screenshot: 'off' });

for (const width of [320, 375, 768, 1024, 1440]) {
  test(`long account names fit the administration header at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 800 });
    await page.route('**/api/**', route => {
      const path = new URL(route.request().url()).pathname;
      const json = path.endsWith('/auth/me')
        ? { id: 'layout-user', userName: 'LongAccountName'.repeat(12), roles: ['Admin'], mustChangePassword: false }
        : path.endsWith('/auth/access') ? { permissions: ['platform.manage'], allApplications: true, applicationIds: [] }
        : path.endsWith('/dashboard') ? { totalLicenses: 123456789012, activatedLicenses: 0, validLicenses: 0, onlineSessions: 0 }
        : { items: [], total: 0 };
      return route.fulfill({ json });
    });
    await page.goto('/overview');
    await expect(page.locator('.account-name strong')).toBeVisible();
    await expect(page.getByRole('button', { name: '退出登录', exact: true })).toBeInViewport();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    const name = await page.locator('.account-name strong').boundingBox();
    const header = await page.locator('.topbar').boundingBox();
    expect(name!.y).toBeGreaterThanOrEqual(header!.y);
    expect(name!.y + name!.height).toBeLessThanOrEqual(header!.y + header!.height);
    await expect.poll(() => page.locator('.app-shell').evaluate(element => getComputedStyle(element).animationName)).toBe('screen-enter');
    await expect.poll(() => page.locator('.app-shell').evaluate(element => element.getAnimations().length)).toBe(0);
  });
}

test('portal content remains reachable in a short landscape viewport', async ({ page }) => {
  await page.setViewportSize({ width: 667, height: 375 });
  await page.addInitScript(() => sessionStorage.setItem('keygate.portal.session.v1', JSON.stringify({
    token: 'layout-token', expiresAt: new Date(Date.now() + 3600000).toISOString(),
  })));
  await page.route('**/api/portal/**', route => route.fulfill({ json: route.request().url().endsWith('/me')
    ? { id: 'customer', name: '长客户名称'.repeat(15) } : { items: [], total: 0 } }));
  await page.goto('/portal/orders');
  const orders = page.getByRole('heading', { name: '我的订单', exact: true });
  await expect(orders).toBeVisible();
  await orders.scrollIntoViewIfNeeded();
  await expect(orders).toBeInViewport();
  await page.locator('.portal-footer').scrollIntoViewIfNeeded();
  await expect(page.locator('.portal-footer')).toBeInViewport();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(667);
});
