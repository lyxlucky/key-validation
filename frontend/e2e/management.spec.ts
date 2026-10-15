import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import path from 'node:path';
import { mkdir } from 'node:fs/promises';

test.use({ trace: 'off', screenshot: 'off' });
test.afterEach(async ({ page }) => { if (!page.isClosed()) await page.goto('about:blank'); });

const previews = path.resolve('..', '.local', 'management-preview');
async function login(page: Page, user = process.env.E2E_ADMIN_USER!, password = process.env.E2E_ADMIN_PASSWORD!, firstLogin = false) {
  await page.goto('/login');
  await page.getByLabel('用户名', { exact: true }).fill(user);
  await page.getByLabel('密码', { exact: true }).fill(password);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  if (firstLogin) {
    await expect(page.getByRole('heading', { name: '账号安全', exact: true })).toBeVisible();
    await page.getByRole('button', { name: '修改密码', exact: true }).click();
    const change = page.getByRole('dialog', { name: '修改密码', exact: true });
    await change.getByLabel('当前密码', { exact: true }).fill(password);
    await change.getByLabel('新密码', { exact: true }).fill(`${password}Changed`);
    await change.getByLabel('确认新密码', { exact: true }).fill(`${password}Changed`);
    await change.getByRole('button', { name: '保存修改', exact: true }).click();
    await expect(page.getByText('请先修改初始密码', { exact: true })).toHaveCount(0);
    await page.goto('/overview');
  }
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
}
async function catalog(page: Page) {
  await login(page);
  const headers = { 'X-CSRF-TOKEN': (await (await page.request.get('/api/admin/auth/csrf')).json()).token };
  const name = `管理验收-${randomUUID().slice(0, 8)}`;
  const appResponse = await page.request.post('/api/admin/applications', { headers, data: { name, code: `manage-${randomUUID().slice(0, 8)}` } });
  expect(appResponse.status()).toBe(201);
  const app = await appResponse.json();
  const plan = await (await page.request.post(`/api/admin/applications/${app.id}/plans`, { headers, data: { name: '验收日卡', durationSeconds: 86400, maxDevices: 1 } })).json();
  const issuanceResponse = await page.request.post('/api/admin/license-batches', { headers, data: { applicationId: app.id, planId: plan.id, quantity: 2, note: name, requestId: randomUUID() } });
  expect(issuanceResponse.status()).toBe(201);
  const issuance = await issuanceResponse.json();
  const activationResponse = await page.request.post('/api/client/licenses/activate', { data: { appCode: app.code, licenseKey: issuance.licenses[0].key, deviceId: randomUUID(), deviceLabel: '管理验收设备' } });
  expect(activationResponse.status()).toBe(200);
  const activated = await activationResponse.json();
  return { app, name, issuance, activated, headers };
}
async function finishOperation(page: Page, count: number) {
  await page.getByLabel('操作原因', { exact: true }).fill('浏览器验收');
  await page.getByRole('button', { name: '确认执行', exact: true }).click();
  const result = page.getByRole('dialog', { name: '操作结果', exact: true });
  await expect(result.getByText(`成功 ${count} 张，失败 0 张`, { exact: true })).toBeVisible();
  await result.getByRole('button', { name: '完成', exact: true }).click();
}

test('account management creates, changes roles, resets password and disables access', async ({ page, browser }) => {
  const name = `account-${randomUUID().slice(0, 8)}`;
  const password = `${randomUUID()}Aa1!`;
  await login(page);
  await page.getByRole('menuitem', { name: '系统管理', exact: true }).click();
  await page.getByRole('menuitem', { name: '账号管理' }).click();
  await page.getByRole('button', { name: /创建账号$/ }).click();
  const create = page.getByRole('dialog', { name: '创建账号', exact: true });
  await create.getByLabel('用户名', { exact: true }).fill(name);
  await create.getByLabel('初始密码', { exact: true }).fill(password);
  await create.getByLabel('账号角色', { exact: true }).click();
  await page.getByTitle('只读', { exact: true }).click();
  await create.getByLabel('账号角色', { exact: true }).press('Escape');
  await create.getByRole('button', { name: '创建账号', exact: true }).click();
  const row = page.getByRole('row').filter({ hasText: name });
  await expect(row.getByText('只读', { exact: true })).toBeVisible();
  const readerContext = await browser.newContext({ baseURL: 'http://127.0.0.1:5173' });
  try {
    const reader = await readerContext.newPage();
    await login(reader, name, password, true);
    await expect(reader.getByRole('menuitem', { name: '账号管理' })).toHaveCount(0);
    await reader.goto('/licenses');
    await expect(reader.getByRole('heading', { name: '卡密管理', exact: true })).toBeVisible();
    await expect(reader.getByRole('button', { name: '批量续期' })).toHaveCount(0);
    await row.getByRole('button', { name: '调整角色' }).click();
    await page.getByLabel('账号角色').click();
    await page.getByTitle('操作员', { exact: true }).click();
    await page.getByLabel('账号角色').press('Escape');
    await page.getByRole('button', { name: '保存修改' }).click();
    await expect(row.getByText('操作员', { exact: true })).toBeVisible();
    expect((await reader.request.get('/api/admin/auth/me')).status()).toBe(401);
    await row.getByRole('button', { name: '重置密码' }).click();
    await page.getByLabel('新密码', { exact: true }).fill(`${password}New`);
    await page.getByRole('button', { name: '保存修改' }).click();
    await expect(page.getByRole('dialog', { name: '重置密码', exact: true })).toBeHidden();
    await row.getByRole('button', { name: '停用账号' }).click();
    await page.getByRole('button', { name: '确认停用账号' }).click();
    await expect(row.getByText('已停用', { exact: true })).toBeVisible();
    await row.getByRole('button', { name: '恢复账号' }).click();
    await page.getByRole('button', { name: '确认恢复账号' }).click();
    await expect(row.getByText('已启用', { exact: true })).toBeVisible();
    await mkdir(previews, { recursive: true });
    await page.screenshot({ path: path.join(previews, 'accounts-desktop.png'), animations: 'disabled' });
  } finally { await readerContext.close(); }
});

test('batch, metadata, renewal, expiry and bulk controls work together', async ({ page }) => {
  const { app, name, issuance, activated } = await catalog(page);
  await page.goto('/batches');
  await page.getByLabel('批次备注', { exact: true }).fill(name);
  await page.getByRole('button', { name: '筛选', exact: false }).click();
  const batchRow = page.getByRole('row').filter({ hasText: name });
  await expect(batchRow).toHaveCount(1);
  await batchRow.getByRole('button', { name: '详情', exact: true }).click();
  await expect(page.getByRole('dialog', { name: '批次详情' })).toBeVisible();
  await page.getByRole('dialog', { name: '批次详情' }).getByRole('button', { name: '查看卡密' }).click();
  const licensePane = page.getByRole('tabpanel', { name: '卡密管理', exact: true });
  await expect(page.getByLabel('发行批次')).toHaveValue(issuance.batch.id);
  const licenseRow = page.getByRole('row').filter({ hasText: issuance.licenses[0].keyHint });
  await licenseRow.getByRole('button', { name: '详情', exact: true }).click();
  const detail = page.getByRole('dialog', { name: '卡密详情', exact: true });
  await detail.getByRole('button', { name: '编辑备注标签', exact: true }).click();
  const metadata = page.getByRole('dialog', { name: '编辑备注标签', exact: true });
  await metadata.getByLabel('卡密备注', { exact: true }).fill('客户续费验收');
  await metadata.getByLabel('标签', { exact: true }).fill('重要客户');
  await metadata.getByLabel('标签', { exact: true }).press('Enter');
  await finishOperation(page, 1);
  await expect(detail.getByText('客户续费验收', { exact: true })).toBeVisible();
  await detail.getByRole('button', { name: '续期卡密', exact: true }).click();
  await page.getByLabel('延长时长（小时）').fill('1');
  await finishOperation(page, 1);
  const renewed = await (await page.request.get(`/api/admin/licenses/${issuance.licenses[0].id}`)).json();
  expect(Date.parse(renewed.license.expiresAt) - Date.parse(activated.expiresAt)).toBe(3600000);
  await detail.getByRole('button', { name: '关闭', exact: true }).click();
  await page.getByLabel('备注关键字').fill('客户续费');
  await page.getByLabel('标签', { exact: true }).fill('重要客户');
  await page.getByRole('button', { name: '筛选', exact: true }).click();
  await expect(licensePane.locator('.data-panel .ant-table-tbody > tr[data-row-key]')).toHaveCount(1);

  await page.goto(`/licenses?batchId=${issuance.batch.id}`);
  await expect(licensePane.locator('.data-panel .ant-table-tbody > tr[data-row-key]')).toHaveCount(2);
  await licensePane.locator('.data-panel .ant-table-thead').getByRole('checkbox').check();
  await page.getByRole('button', { name: '批量禁用', exact: true }).click();
  await finishOperation(page, 2);
  let rows = await (await page.request.get(`/api/admin/licenses?batchId=${issuance.batch.id}`)).json();
  expect(rows.items.every((x: { status: string }) => x.status === 'disabled')).toBe(true);
  await page.getByRole('button', { name: '批量恢复', exact: true }).click();
  await finishOperation(page, 2);
  await page.getByRole('button', { name: '批量续期', exact: true }).click();
  await page.getByLabel('延长时长（小时）').fill('1');
  await finishOperation(page, 2);
  await page.getByRole('button', { name: '批量编辑备注标签', exact: true }).click();
  await page.getByRole('dialog', { name: '批量编辑备注标签', exact: true }).getByLabel('卡密备注', { exact: true }).fill('批量客户备注');
  await finishOperation(page, 2);
  rows = await (await page.request.get(`/api/admin/licenses?batchId=${issuance.batch.id}`)).json();
  expect(rows.items.every((x: { note: string }) => x.note === '批量客户备注')).toBe(true);

  await page.goto('/expiring');
  await page.getByLabel('应用', { exact: true }).click();
  await page.getByTitle(app.name, { exact: true }).click();
  await page.getByText('3 天内', { exact: true }).click();
  await expect(page.getByRole('cell', { name: issuance.licenses[0].keyHint, exact: true })).toBeVisible();
  await expect(page.getByText('查询窗口：服务端当前时间起 3 天内（每天 24 小时）')).toBeVisible();
  await mkdir(previews, { recursive: true });
  await page.screenshot({ path: path.join(previews, 'expiry-desktop.png'), animations: 'disabled' });
});

test('operation recovery survives reload and a rejected retry without double renewal', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  const { issuance, activated } = await catalog(page);
  await page.goto(`/licenses?batchId=${issuance.batch.id}`);
  await page.evaluate(() => sessionStorage.setItem('keygate.pendingOperation.foreign-account', JSON.stringify({ requestId: crypto.randomUUID(), licenseIds: [crypto.randomUUID()], action: 'disable', reason: 'foreign' })));
  await page.reload();
  await expect(page.getByRole('button', { name: '处理待确认操作' })).toHaveCount(0);
  await page.getByRole('row').filter({ hasText: issuance.licenses[0].keyHint }).getByRole('button', { name: '详情', exact: true }).click();
  await page.getByRole('button', { name: '续期卡密', exact: true }).click();
  await page.getByLabel('延长时长（小时）').fill('1');
  await page.getByLabel('操作原因').fill('恢复验收');
  await expect(page.getByRole('button', { name: '确认执行', exact: true })).toBeInViewport();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
  let submissions = 0;
  await page.route('**/api/admin/license-operations', async route => {
    if (route.request().method() !== 'POST') return route.continue();
    submissions++;
    if (submissions === 1) { expect((await route.fetch()).status()).toBe(200); await route.abort('failed'); }
    else await route.fulfill({ status: 429, contentType: 'application/problem+json', body: JSON.stringify({ code: 'RATE_LIMITED', status: 429 }) });
  });
  await page.getByRole('button', { name: '确认执行' }).click();
  await expect(page.getByText('连接中断，操作结果尚未确认。请检查操作结果，或使用原请求重试。')).toBeVisible();
  await page.reload();
  await page.getByRole('button', { name: '处理待确认操作' }).click();
  await expect(page.getByText('延长时长：1 小时', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: '使用原请求重试' }).click();
  await expect(page.getByText('操作过于频繁，请稍后重试', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: '检查操作结果' }).click();
  await expect(page.getByText('成功 1 张，失败 0 张', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: '完成', exact: true })).toBeInViewport();
  await mkdir(previews, { recursive: true });
  await page.screenshot({ path: path.join(previews, 'operation-result-375.png'), animations: 'disabled' });
  const result = await (await page.request.get(`/api/admin/licenses/${issuance.licenses[0].id}`)).json();
  expect(Date.parse(result.license.expiresAt) - Date.parse(activated.expiresAt)).toBe(3600000);
  await expect(page.getByRole('button', { name: '处理待确认操作' })).toHaveCount(0);
});

test('license and expiry lists recover when the last page becomes empty', async ({ page }) => {
  await login(page);
  const serverTime = new Date().toISOString();
  const items = Array.from({ length: 21 }, (_, index) => ({
    id: randomUUID(), applicationId: randomUUID(), batchId: randomUUID(), keyHint: `PAGE-${index + 1}`,
    durationSeconds: 86400, maxDevices: 1, status: 'active', createdAt: serverTime,
    activatedAt: serverTime, expiresAt: new Date(Date.now() + 86400000).toISOString(), disabledAt: null, note: null, tags: [],
  }));
  for (const [routeName, apiName] of [['licenses', 'licenses'], ['expiring', 'expiring-licenses']]) {
    let total = 21;
    await page.route(`**/api/admin/${apiName}?**`, async route => {
      const current = Number(new URL(route.request().url()).searchParams.get('page') || 1);
      await route.fulfill({ json: { items: items.slice(0, total).slice((current - 1) * 20, current * 20), total, page: current, pageSize: 20, serverTime, withinDays: 7 } });
    });
    await page.goto(`/${routeName}`);
    await expect(page.locator('.data-panel .ant-table-tbody > tr[data-row-key]')).toHaveCount(20);
    await page.locator('.ant-pagination-item-2').click();
    await expect(page.getByRole('cell', { name: 'PAGE-21', exact: true })).toBeVisible();
    total = 20;
    await page.getByRole('button', { name: '刷新', exact: false }).click();
    await expect(page.locator('.data-panel .ant-table-tbody > tr[data-row-key]')).toHaveCount(20);
    await expect(page.locator('.ant-pagination-item-1')).toHaveClass(/ant-pagination-item-active/);
    await page.unroute(`**/api/admin/${apiName}?**`);
  }
});
