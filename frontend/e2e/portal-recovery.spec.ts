import { test, expect } from '@playwright/test';

test.use({ reducedMotion: 'reduce', trace: 'off', screenshot: 'off' });
test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => sessionStorage.setItem('keygate.portal.session.v1', JSON.stringify({
    token: 'synthetic-portal-token', expiresAt: new Date(Date.now() + 3600000).toISOString(),
  })));
  await page.route('**/api/portal/**', route => route.fulfill({ json:
    route.request().url().endsWith('/me') ? { id: 'customer-1', name: '测试客户' } : { items: [], total: 0 },
  }));
});

test('restoring a session can retry a temporary failure without an access code', async ({ page }) => {
  let connected = false;
  await page.route('**/api/portal/me', route => !connected
    ? route.abort('failed') : route.fulfill({ json: { id: 'customer-1', name: '测试客户' } }));
  await page.goto('/portal/orders');
  await expect(page.getByRole('button', { name: '重试连接' })).toBeVisible();
  await expect(page.getByLabel('客户访问码')).toHaveCount(0);
  connected = true;
  await page.getByRole('button', { name: '重试连接' }).click();
  await expect(page.getByRole('heading', { name: '测试客户' })).toBeVisible();
});

test('failed logout still clears local credentials and customer content', async ({ page }) => {
  await page.route('**/api/portal/sessions', route => route.abort('failed'));
  await page.goto('/portal/orders');
  await page.getByRole('button', { name: '退出自助服务' }).click();
  await expect(page.getByLabel('客户访问码')).toBeVisible();
  await expect(page.getByRole('heading', { name: '测试客户' })).toHaveCount(0);
  expect(await page.evaluate(() => sessionStorage.getItem('keygate.portal.session.v1'))).toBeNull();
});

test('an expired session returns to access-code login instead of offering connection retries', async ({ page }) => {
  await page.route('**/api/portal/me', route => route.fulfill({ status: 401, json: { code: 'UNAUTHORIZED' } }));
  await page.goto('/portal/orders');
  await expect(page.getByLabel('客户访问码')).toBeVisible();
  await expect(page.getByRole('button', { name: '重试连接' })).toHaveCount(0);
  expect(await page.evaluate(() => sessionStorage.getItem('keygate.portal.session.v1'))).toBeNull();
});

for (const path of ['toString', 'constructor', '__proto__']) {
  test(`inherited property ${path} redirects to orders`, async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(`/portal/${path}`);
    await expect(page).toHaveURL(/\/portal\/orders$/);
    await expect(page.getByRole('heading', { name: '我的订单', exact: true })).toBeVisible();
    expect(errors).toEqual([]);
  });
}
