import { test, expect } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { catalog, login } from './roadmap-helpers';

// These workflows issue credentials; never retain network traces or automatic screenshots.
test.use({ trace: 'off', screenshot: 'off' });

test('advanced license search keeps device and activation filters on the next page', async ({ page }) => {
  const { app, issuance } = await catalog(page, 22);
  const deviceId = `device-${randomUUID()}`;
  for (const license of issuance.licenses.slice(0, 21)) {
    const activation = await page.request.post('/api/client/licenses/activate', { data: { appCode: app.code, licenseKey: license.key, deviceId } });
    expect(activation.status()).toBe(200);
  }
  await page.goto('/licenses');
  await page.getByLabel('筛选应用', { exact: true }).fill(app.name);
  await page.getByTitle(app.name, { exact: true }).click();
  await page.getByRole('button', { name: '高级筛选', exact: true }).click();
  await page.getByLabel('设备标识（精确匹配）', { exact: true }).fill(deviceId);
  const today = new Date();
  const day = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
  await page.getByLabel('激活起始日', { exact: true }).fill(day);
  await page.getByRole('button', { name: '筛选', exact: true }).click();
  await expect(page.getByText('共 21 张卡密', { exact: true })).toBeVisible();
  await page.locator('.ant-pagination-item-2').click();
  await expect(page.locator('.data-panel .ant-table-tbody > tr[data-row-key]')).toHaveCount(1);
  const result = await page.request.post('/api/admin/licenses/query', { headers: { 'X-CSRF-TOKEN': (await (await page.request.get('/api/admin/auth/csrf')).json()).token }, data: { applicationId: app.id, deviceId, page: 2, pageSize: 20 } });
  expect(result.status()).toBe(200);
  const secondPage = await result.json();
  await expect(page.getByRole('cell', { name: secondPage.items[0].keyHint, exact: true })).toBeVisible();
  await page.getByRole('button', { name: '刷新卡密', exact: true }).click();
  await expect(page.getByText('共 21 张卡密', { exact: true })).toBeVisible();
  await expect(page.locator('.data-panel .ant-table-tbody > tr[data-row-key]')).toHaveCount(1);
});

test('operations report shows actual scoped issuance and distinct online counts', async ({ page }) => {
  const { app, issuance } = await catalog(page);
  const activation = await page.request.post('/api/client/licenses/activate', { data: { appCode: app.code, licenseKey: issuance.licenses[0].key, deviceId: randomUUID() } });
  expect(activation.status()).toBe(200);
  await page.goto('/reports');
  await expect(page.getByRole('heading', { name: '运营统计', exact: true })).toBeVisible();
  await page.getByLabel('统计应用', { exact: true }).fill(app.name);
  await page.getByTitle(app.name, { exact: true }).click();
  await expect(page.getByTestId('report-issued')).toHaveText('2');
  await expect(page.getByTestId('report-activated')).toHaveText('1');
  await expect(page.getByTestId('report-onlineLicenses')).toHaveText('1');
  await expect(page.getByTestId('report-onlineSessions')).toHaveText('1');
  await expect(page.getByText('统计时区：UTC', { exact: true })).toBeVisible();
});

test('assigned roles supply permissions while the account editor saves independent application scope', async ({ page, browser }) => {
  const { headers, app } = await catalog(page);
  const name = `scope-${randomUUID().slice(0, 8)}`;
  const password = `${randomUUID()}Aa1!`;
  const roleName = `scoped-reader-${randomUUID().slice(0, 8)}`;
  const roleResponse = await page.request.post('/api/admin/roles', { headers, data: { name: roleName, permissions: ['licenses.read', 'reports.read'] } });
  expect(roleResponse.status()).toBe(201);
  const role = await roleResponse.json();
  const response = await page.request.post('/api/admin/accounts', { headers, data: { userName: name, password, roleIds: [role.id] } });
  expect(response.status()).toBe(201);
  const account = await response.json();
  await page.goto('/accounts');
  await page.getByRole('row').filter({ hasText: name }).getByRole('button', { name: '数据范围', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '应用与渠道范围', exact: true });
  await expect(editor).toBeVisible();
  await expect(editor.getByRole('checkbox')).toHaveCount(0);
  await editor.getByRole('switch', { name: '全部应用', exact: true }).click();
  await editor.getByLabel('允许访问的应用', { exact: true }).fill(app.name);
  await page.getByTitle(app.name, { exact: true }).click();
  await editor.getByLabel('允许访问的应用', { exact: true }).press('Escape');
  await editor.getByRole('button', { name: '保存范围', exact: true }).click();
  await expect(editor).toBeHidden();
  const access = await (await page.request.get(`/api/admin/accounts/${account.id}/access`)).json();
  expect(access.permissions.sort()).toEqual(['licenses.read', 'reports.read']);
  expect(access.allApplications).toBe(false);
  expect(access.applicationIds).toEqual([app.id]);
  const context = await browser.newContext({ baseURL: 'http://127.0.0.1:5173' });
  try {
    const restricted = await context.newPage();
    await login(restricted, name, password, true);
    await restricted.goto('/licenses');
    await expect(restricted.getByRole('heading', { name: '卡密管理', exact: true })).toBeVisible();
    await expect(restricted.getByRole('button', { name: /生成卡密$/ })).toHaveCount(0);
    await expect(restricted.getByRole('button', { name: '批量续期', exact: true })).toHaveCount(0);
    await expect(restricted.getByRole('menuitem', { name: /操作日志$/ })).toHaveCount(0);
    const visibleApps = await (await restricted.request.get('/api/admin/applications')).json();
    expect(visibleApps.items.map((item: { id: string }) => item.id)).toEqual([app.id]);
    await restricted.goto('/audit');
    await expect(restricted.getByText('没有访问权限', { exact: true })).toBeVisible();
  } finally { await context.close(); }
  const narrowed = await page.request.patch(`/api/admin/roles/${role.id}`, { headers, data: { name: roleName, permissions: ['reports.read'] } });
  expect(narrowed.status()).toBe(200);
  const reportContext = await browser.newContext({ baseURL: 'http://127.0.0.1:5173' });
  try {
    const reportsOnly = await reportContext.newPage();
    const denied: string[] = [];
    reportsOnly.on('response', response => { if (response.status() === 403) denied.push(response.url()); });
    await login(reportsOnly, name, `${password}New`);
    await expect(reportsOnly.getByText('最近生成的卡密', { exact: true })).toHaveCount(0);
    await expect(reportsOnly.getByRole('button', { name: /查看操作日志/ })).toHaveCount(0);
    await reportsOnly.goto('/reports');
    await reportsOnly.getByLabel('统计应用', { exact: true }).fill(app.name);
    await reportsOnly.getByTitle(app.name, { exact: true }).click();
    await expect(reportsOnly.getByTestId('report-issued')).toHaveText('2');
    expect(denied).toEqual([]);
  } finally { await reportContext.close(); }
});
