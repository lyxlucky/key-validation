import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

test.use({ trace: 'off', screenshot: 'off' });
test.afterEach(async ({ page }) => { if (!page.isClosed()) await page.goto('about:blank'); });

async function signIn(page: Page) {
  if (!process.env.E2E_ADMIN_USER || !process.env.E2E_ADMIN_PASSWORD) throw new Error('E2E administrator credentials are required.');
  await page.goto('/');
  await page.getByLabel('用户名', { exact: true }).fill(process.env.E2E_ADMIN_USER);
  await page.getByLabel('密码', { exact: true }).fill(process.env.E2E_ADMIN_PASSWORD);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
}

async function prepareIssuance(page: Page) {
  await signIn(page);
  const csrf = await (await page.request.get('/api/admin/auth/csrf')).json();
  const headers = { 'X-CSRF-TOKEN': csrf.token };
  const suffix = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
  const response = await page.request.post('/api/admin/applications', { headers, data: { code: `recover-${suffix}`, name: `恢复测试 ${suffix}` } });
  expect(response.status()).toBe(201);
  const app = await response.json();
  const planResponse = await page.request.post(`/api/admin/applications/${app.id}/plans`, { headers, data: { name: '恢复测试卡', durationSeconds: 86400, maxDevices: 1 } });
  expect(planResponse.status()).toBe(201);
  const plan = await planResponse.json();
  await page.goto('/licenses');
  await page.getByRole('button', { name: '生成卡密' }).click();
  await page.getByLabel('所属应用').click();
  await page.getByTitle(app.name, { exact: true }).click();
  await page.getByLabel('卡种', { exact: true }).click();
  await page.getByTitle(plan.name, { exact: true }).click();
  await expect(page.getByRole('dialog', { name: '生成卡密', exact: true }).getByTitle(plan.name, { exact: true })).toBeVisible();
  await page.getByLabel('生成数量').fill('2');
  return { app, plan, headers };
}

test('login form explains the administration purpose and validates required inputs', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: '登录管理后台' })).toBeVisible();
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByText('请输入用户名', { exact: true })).toBeVisible();
  await expect(page.getByText('请输入密码', { exact: true })).toBeVisible();
});

test('definitive issuance rejection allows correcting the form', async ({ page }) => {
  const { plan, headers } = await prepareIssuance(page);
  const disabled = await page.request.patch(`/api/admin/plans/${plan.id}`, { headers, data: { name: plan.name, durationSeconds: 86400, maxDevices: 1, isEnabled: false } });
  expect(disabled.status()).toBe(200);
  const rejection = page.waitForResponse(response => response.url().endsWith('/api/admin/license-batches') && response.status() === 403);
  await page.getByRole('button', { name: '确认生成' }).click();
  await rejection;
  await expect(page.getByLabel('所属应用')).toBeEnabled();
  await expect(page.getByRole('button', { name: '确认生成' })).toBeEnabled();
});

test('lost issuance response is recovered without duplicate keys', async ({ page }) => {
  const { app } = await prepareIssuance(page);
  let committed = false;
  await page.route('**/api/admin/license-batches', async route => {
    if (route.request().method() === 'POST' && !committed) {
      const response = await route.fetch();
      expect(response.status()).toBe(201);
      committed = true;
      await route.abort('failed');
    } else await route.continue();
  });
  await page.getByRole('button', { name: '确认生成' }).click();
  await expect(page.getByText('存在待确认的发卡请求')).toBeVisible();
  await expect(page.getByText('网络连接失败。若正在生成卡密，请先检查批次是否已创建。', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: '检查已创建批次' }).click();
  await expect(page.getByText(/共 2 张。明文无法恢复/)).toBeVisible();
  const batches = await (await page.request.get(`/api/admin/license-batches?applicationId=${app.id}`)).json();
  expect(batches.total).toBe(1);
  await page.getByRole('button', { name: '禁用原批次' }).click();
  await page.getByRole('button', { name: '确认禁用批次' }).click();
  await expect(page.getByText('存在待确认的发卡请求')).toHaveCount(0);
  const licenses = await (await page.request.get(`/api/admin/licenses?applicationId=${app.id}`)).json();
  expect(licenses.items).toHaveLength(2);
  expect(licenses.items.every((license: { status: string }) => license.status === 'disabled')).toBe(true);
  await expect(page.getByLabel('所属应用')).toBeEnabled();
});

test('another account pending request is never loaded into this account', async ({ page }) => {
  await signIn(page);
  await page.evaluate(() => sessionStorage.setItem('keygate.pendingBatch', JSON.stringify({ applicationId: '11111111-1111-1111-1111-111111111111', planId: '22222222-2222-2222-2222-222222222222', quantity: 2, requestId: '33333333-3333-3333-3333-333333333333' })));
  await page.goto('/licenses');
  await page.getByRole('button', { name: '生成卡密' }).click();
  await expect(page.getByText('存在待确认的发卡请求')).toHaveCount(0);
  await expect(page.getByLabel('所属应用')).toBeEnabled();
});

test('a rate-limited recovery retry preserves the original committed request', async ({ page }) => {
  const { app } = await prepareIssuance(page);
  let submissions = 0;
  await page.route('**/api/admin/license-batches', async route => {
    if (route.request().method() !== 'POST') return route.continue();
    submissions++;
    if (submissions === 1) {
      const response = await route.fetch();
      expect(response.status()).toBe(201);
      await route.abort('failed');
    } else await route.fulfill({ status: 429, contentType: 'application/problem+json', body: JSON.stringify({ status: 429, code: 'RATE_LIMITED' }) });
  });
  await page.getByRole('button', { name: '确认生成' }).click();
  await expect(page.getByText('网络连接失败。若正在生成卡密，请先检查批次是否已创建。', { exact: true })).toBeVisible();
  const requestId = await page.evaluate(() => {
    const key = Object.keys(sessionStorage).find(x => x.startsWith('keygate.pendingBatch.'))!;
    return JSON.parse(sessionStorage.getItem(key)!).requestId as string;
  });
  await page.getByRole('button', { name: '使用原请求重试' }).click();
  await expect(page.getByText('操作过于频繁，请稍后重试', { exact: true })).toBeVisible();
  await expect(page.getByText(requestId, { exact: true })).toBeVisible();
  await page.getByRole('button', { name: '检查已创建批次' }).click();
  await expect(page.getByText(/共 2 张。明文无法恢复/)).toBeVisible();
  const batches = await (await page.request.get(`/api/admin/license-batches?applicationId=${app.id}`)).json();
  expect(batches.total).toBe(1);
});

test('real API: issue a license, activate, disable and inspect audit', async ({ page, request }) => {
  const user = process.env.E2E_ADMIN_USER;
  const password = process.env.E2E_ADMIN_PASSWORD;
  if (!user || !password) throw new Error('Set E2E_ADMIN_USER and E2E_ADMIN_PASSWORD to run real API tests.');
  const suffix = Date.now().toString(36);
  const appName = `测试应用 ${suffix}`;
  const appCode = `e2e-${suffix}`;
  await page.goto('/');
  await page.getByLabel('用户名', { exact: true }).fill(user);
  await page.getByLabel('密码', { exact: true }).fill(password);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
  await page.getByRole('menuitem', { name: '授权管理', exact: true }).click();
  await page.getByRole('menuitem', { name: '应用管理' }).click();
  await page.getByRole('button', { name: '创建应用' }).click();
  await page.getByLabel('应用名称').fill(appName);
  await page.getByLabel('应用标识', { exact: true }).fill(appCode);
  await page.getByRole('button', { name: '创建', exact: true }).click();
  const row = page.getByRole('row').filter({ hasText: appName });
  await expect(row).toBeVisible();
  await page.goto('/plans');
  await page.getByRole('button', { name: '创建卡种' }).click();
  const planName = '月卡-' + suffix;
  await page.getByLabel('卡种名称').fill(planName);
  await page.getByRole('button', { name: '保存卡种' }).click();
  await expect(page.getByRole('cell').filter({ hasText: planName })).toBeVisible();
  await page.goto('/applications');
  await row.getByRole('button', { name: '应用设置', exact: true }).click();
  const settings = page.getByRole('dialog', { name: `${appName} · 应用设置`, exact: true });
  await settings.getByLabel('选择全局卡种').fill(planName);
  await page.getByTitle(new RegExp('^' + planName + ' · ')).click();
  await settings.getByRole('button', { name: '引用卡种', exact: true }).click();
  await expect(settings.getByRole('cell', { name: planName, exact: true })).toBeVisible();
  await settings.getByRole('button', { name: '关闭', exact: true }).click();
  await page.getByRole('menuitem', { name: '卡密管理' }).click();
  await page.getByRole('button', { name: '生成卡密' }).click();
  await page.getByLabel('所属应用').click();
  await page.getByTitle(appName, { exact: true }).click();
  await page.getByLabel('卡种', { exact: true }).click();
  await page.getByTitle(planName, { exact: true }).click();
  await page.getByRole('button', { name: '确认生成' }).click();
  await expect(page.getByText('卡密仅在此显示一次')).toBeVisible();
  const key = await page.getByTestId('issued-key').first().innerText();
  const previews = path.resolve('..', '.local', 'ui-preview');
  await mkdir(previews, { recursive: true });
  await page.screenshot({ path: path.join(previews, 'issued-result-desktop.png'), mask: [page.getByTestId('issued-key')], animations: 'disabled' });
  await page.setViewportSize({ width: 375, height: 812 });
  await expect(page.getByRole('button', { name: '已保存，关闭' })).toBeInViewport();
  await page.screenshot({ path: path.join(previews, 'issued-result-375.png'), mask: [page.getByTestId('issued-key')], animations: 'disabled' });
  await page.setViewportSize({ width: 1280, height: 720 });
  const downloadEvent = page.waitForEvent('download');
  await page.getByRole('button', { name: '下载 CSV' }).click();
  expect((await downloadEvent).suggestedFilename()).toMatch(/\.csv$/);
  await page.getByRole('button', { name: '已保存，关闭' }).click();
  const activation = await request.post('/api/client/licenses/activate', { data: { appCode, licenseKey: key, deviceId: `device-${suffix}`, deviceLabel: '浏览器集成测试' } });
  expect(activation.status()).toBe(200);
  const session = await activation.json();
  expect((await request.post('/api/client/sessions/heartbeat', { headers: { Authorization: `Bearer ${session.sessionToken}` } })).status()).toBe(200);
  await page.getByPlaceholder('输入完整卡密精确查找').fill(key);
  await page.getByRole('button', { name: '查找', exact: true }).click();
  await expect(page.getByText('浏览器集成测试', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: '禁用卡密', exact: true }).click();
  const disabledResponse = page.waitForResponse(response => /\/api\/admin\/licenses\/[^/]+\/disable$/.test(response.url()) && response.request().method() === 'POST');
  await page.getByRole('button', { name: '确认禁用', exact: true }).click();
  expect((await disabledResponse).status()).toBe(204);
  const heartbeat = await request.post('/api/client/sessions/heartbeat', { headers: { Authorization: `Bearer ${session.sessionToken}` } });
  expect([401, 403]).toContain(heartbeat.status());
  await page.getByRole('dialog', { name: '卡密详情', exact: true }).getByRole('button', { name: '关闭', exact: true }).click();
  await page.getByRole('menuitem', { name: '系统管理', exact: true }).click();
  await page.getByRole('menuitem', { name: '操作日志' }).click();
  await expect(page.getByText('禁用卡密', { exact: true }).first()).toBeVisible();
});

test('mobile overview stays within the viewport', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await signIn(page);
  await expect(page.locator('.sidebar')).toHaveCount(0);
  await expect(page.getByRole('button', { name: '打开导航' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
});

