import { test, expect } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { catalog } from './roadmap-helpers';

test.use({ trace: 'off', screenshot: 'off' });
test.afterEach(async ({ page }) => { if (!page.isClosed()) await page.goto('about:blank'); });

for (const permission of ['licenses.read', 'platform.manage']) {
  test(permission + ' grants only the corresponding client administration page and actions', async ({ page, browser }) => {
    const { app, headers } = await catalog(page, 1);
    const suffix = randomUUID().slice(0, 8);
    const roleResponse = await page.request.post('/api/admin/roles', { headers, data: { name: '客户端权限-' + suffix, permissions: [permission] } });
    expect(roleResponse.status()).toBe(201);
    const role = await roleResponse.json();
    const userName = 'client-policy-' + suffix, password = randomUUID() + 'aA1!';
    const account = await page.request.post('/api/admin/accounts', { headers, data: { userName, password, roleIds: [role.id] } });
    expect(account.status()).toBe(201);
    const context = await browser.newContext({ baseURL: 'http://127.0.0.1:5173' });
    try {
      const csrf = async () => ({ 'X-CSRF-TOKEN': (await (await context.request.get('/api/admin/auth/csrf')).json()).token });
      expect((await context.request.post('/api/admin/auth/login', { headers: await csrf(), data: { userName, password } })).status()).toBe(200);
      expect((await context.request.post('/api/admin/security/password', { headers: await csrf(), data: { currentPassword: password, newPassword: password + 'Changed' } })).status()).toBe(200);
      const staff = await context.newPage();
      const forbidden: string[] = [];
      staff.on('response', response => { if (response.status() === 403) forbidden.push(new URL(response.url()).pathname); });
      if (permission === 'licenses.read') {
        await staff.goto('/offline-licenses');
        await expect(staff.getByRole('heading', { name: '离线授权', exact: true })).toBeVisible();
        await expect(staff.getByRole('button', { name: '导出验签公钥', exact: true })).toBeEnabled();
        await expect(staff.getByRole('button', { name: '签发离线授权', exact: true })).toHaveCount(0);
        await expect(staff.getByRole('menuitem', { name: '客户端版本', exact: true })).toHaveCount(0);
        await staff.goto('/client-policies');
        await expect(staff.getByText('没有访问权限', { exact: true })).toBeVisible();
      } else {
        await staff.goto('/client-policies');
        await expect(staff.getByRole('heading', { name: '客户端版本', exact: true })).toBeVisible();
        await staff.getByLabel('版本策略应用', { exact: true }).fill(app.name);
        await staff.getByTitle(app.name, { exact: true }).click();
        await staff.getByLabel('最新版本', { exact: true }).fill('4.0.0');
        await staff.getByRole('button', { name: '保存版本策略', exact: true }).click();
        await expect(staff.getByText('版本策略已保存', { exact: true })).toBeVisible();
        await expect(staff.getByRole('menuitem', { name: '离线授权', exact: true })).toHaveCount(0);
      }
      expect(forbidden).toEqual([]);
    } finally { await context.close(); }
  });
}
