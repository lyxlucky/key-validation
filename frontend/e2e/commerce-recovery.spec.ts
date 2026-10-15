import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { catalog } from './roadmap-helpers';

test.use({ trace: 'off', screenshot: 'off' });
test.afterEach(async ({ page }) => { if (!page.isClosed()) await page.goto('about:blank'); });

async function firstLogin(page: Page, userName: string, password: string, route: string) {
  await page.goto('/login');
  await page.getByLabel('用户名', { exact: true }).fill(userName);
  await page.getByLabel('密码', { exact: true }).fill(password);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByRole('heading', { name: '账号安全', exact: true })).toBeVisible();
  await page.getByRole('button', { name: '修改密码', exact: true }).click();
  const change = page.getByRole('dialog', { name: '修改密码', exact: true });
  await change.getByLabel('当前密码', { exact: true }).fill(password);
  await change.getByLabel('新密码', { exact: true }).fill(`${password}Changed`);
  await change.getByLabel('确认新密码', { exact: true }).fill(`${password}Changed`);
  await change.getByRole('button', { name: '保存修改', exact: true }).click();
  await expect(change).toBeHidden();
  await page.goto(route);
}

async function fillOrder(page: Page, appName: string, customerName: string, planName: string) {
  await page.getByRole('button', { name: '新建订单', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '新建订单', exact: true });
  await editor.getByLabel('订单应用', { exact: true }).fill(appName);
  await page.getByTitle(appName, { exact: true }).click();
  await editor.getByLabel('订单客户', { exact: true }).fill(customerName);
  await page.getByTitle(customerName, { exact: true }).click();
  await editor.getByLabel('订单卡种', { exact: true }).fill(planName);
  await page.getByTitle(planName, { exact: true }).click();
  await editor.getByLabel('单价（分）', { exact: true }).fill('1250');
  return editor;
}

test('an ambiguous order save recovers the original request after closing the editor', async ({ page }) => {
  const { app, plan, headers } = await catalog(page, 1);
  const name = `订单恢复-${randomUUID().slice(0, 8)}`;
  const createdCustomer = await page.request.post('/api/admin/customers', { headers, data: { applicationId: app.id, name } });
  expect(createdCustomer.status()).toBe(201);
  const customer = await createdCustomer.json();
  await page.goto('/orders');
  const editor = await fillOrder(page, app.name, name, plan.name);
  let posts = 0;
  await page.route('**/api/admin/orders', async route => {
    if (route.request().method() !== 'POST') return route.continue();
    posts++;
    const response = await route.fetch();
    expect(response.status()).toBe(201);
    await route.abort('failed');
  });
  await editor.getByRole('button', { name: '保存订单', exact: true }).click();
  await expect(editor.getByText('已保留原订单内容', { exact: true })).toBeVisible();
  await editor.getByRole('button', { name: '关闭', exact: true }).last().click();
  await expect(editor).toBeHidden();
  await page.getByRole('button', { name: '继续处理', exact: true }).click();
  await expect(editor.getByLabel('单价（分）', { exact: true })).toHaveValue('1250');
  await editor.getByRole('button', { name: '查询并重试原订单', exact: true }).click();
  await expect(editor).toBeHidden();
  expect(posts).toBe(1);
  const orders = await (await page.request.get(`/api/admin/orders?customerId=${customer.id}`)).json();
  expect(orders.total).toBe(1);
  expect(orders.items[0].totalCents).toBe(1250);
});

test('orders-only staff can create and fulfill without customer-contact or licensing permissions', async ({ page, browser }) => {
  const { app, plan, headers } = await catalog(page, 1);
  const suffix = randomUUID().slice(0, 8);
  const customerName = `专职订单客户-${suffix}`;
  expect((await page.request.post('/api/admin/customers', { headers, data: { applicationId: app.id, name: customerName, email: 'private@example.test' } })).status()).toBe(201);
  const roleResponse = await page.request.post('/api/admin/roles', { headers, data: { name: `订单专员-${suffix}`, permissions: ['orders.manage'] } });
  expect(roleResponse.status()).toBe(201);
  const role = await roleResponse.json();
  const userName = `orders-${suffix}`, password = `${randomUUID()}aA1!`;
  const accountResponse = await page.request.post('/api/admin/accounts', { headers, data: { userName, password, roleIds: [role.id] } });
  expect(accountResponse.status()).toBe(201);
  const account = await accountResponse.json();
  expect((await page.request.put(`/api/admin/accounts/${account.id}/access`, { headers, data: { allApplications: false, applicationIds: [app.id] } })).ok()).toBe(true);
  const context = await browser.newContext({ baseURL: 'http://127.0.0.1:5173' });
  try {
    const staff = await context.newPage();
    await firstLogin(staff, userName, password, '/orders');
    const forbidden: string[] = [];
    staff.on('response', response => { if (response.status() === 403) forbidden.push(new URL(response.url()).pathname); });
    await expect(staff.getByRole('heading', { name: '订单管理', exact: true })).toBeVisible();
    const editor = await fillOrder(staff, app.name, customerName, plan.name);
    await editor.getByRole('button', { name: '保存订单', exact: true }).click();
    await expect(editor).toBeHidden();
    await staff.getByRole('row').filter({ hasText: customerName }).getByRole('button', { name: '详情', exact: true }).click();
    const detail = staff.getByRole('dialog', { name: '订单详情', exact: true });
    await detail.getByRole('button', { name: '确认履约', exact: true }).click();
    await staff.getByRole('button', { name: '完成履约', exact: true }).click();
    await expect(detail).toContainText('已履约');
    await expect(staff.getByRole('menuitem', { name: '客户管理', exact: true })).toHaveCount(0);
    expect(forbidden).toEqual([]);
    expect((await staff.locator('body').textContent())!.includes('private@example.test')).toBe(false);
  } finally { await context.close(); }
});

test('platform management reaches API credentials without receiving Webhook grants', async ({ page, browser }) => {
  const { app, headers } = await catalog(page, 1);
  const suffix = randomUUID().slice(0, 8);
  const roleResponse = await page.request.post('/api/admin/roles', { headers, data: { name: `平台维护-${suffix}`, permissions: ['platform.manage'] } });
  expect(roleResponse.status()).toBe(201);
  const role = await roleResponse.json();
  const userName = `platform-${suffix}`, password = `${randomUUID()}aA1!`;
  const accountResponse = await page.request.post('/api/admin/accounts', { headers, data: { userName, password, roleIds: [role.id] } });
  expect(accountResponse.status()).toBe(201);
  const context = await browser.newContext({ baseURL: 'http://127.0.0.1:5173' });
  try {
    const manager = await context.newPage();
    await firstLogin(manager, userName, password, '/integrations');
    const forbidden: string[] = [];
    manager.on('response', response => { if (response.status() === 403) forbidden.push(new URL(response.url()).pathname); });
    await expect(manager.getByRole('heading', { name: '开放集成', exact: true })).toBeVisible();
    await expect(manager.getByRole('tab', { name: 'Webhook', exact: true })).toHaveCount(0);
    await manager.getByRole('tab', { name: 'API 凭证', exact: true }).click();
    await manager.getByRole('button', { name: '创建凭证', exact: true }).click();
    const editor = manager.getByRole('dialog', { name: '创建 API 凭证', exact: true });
    await editor.getByLabel('凭证应用', { exact: true }).fill(app.name);
    await expect(manager.getByTitle(app.name, { exact: true })).toBeVisible();
    expect(forbidden).toEqual([]);
    await editor.getByRole('button', { name: '取消', exact: true }).click();
  } finally { await context.close(); }
});

test('a lost quota adjustment response can be retried after closing without adding quota twice', async ({ page }) => {
  const { app, headers } = await catalog(page, 1);
  const suffix = randomUUID().slice(0, 8);
  const accountResponse = await page.request.post('/api/admin/accounts', { headers, data: { userName: `quota-${suffix}`, password: `${randomUUID()}aA1!`, role: 'Operator' } });
  expect(accountResponse.status()).toBe(201);
  const account = await accountResponse.json();
  const name = `额度恢复-${suffix}`;
  const channelResponse = await page.request.post('/api/admin/channels', { headers, data: { name, accountId: account.id, applicationIds: [app.id], initialQuota: 3 } });
  expect(channelResponse.status()).toBe(201);
  const channel = await channelResponse.json();
  await page.goto('/channels');
  await page.getByLabel('搜索渠道', { exact: true }).fill(name);
  await page.getByRole('row').filter({ hasText: name }).getByRole('button', { name: '详情', exact: true }).click();
  const detail = page.getByRole('dialog', { name: '渠道详情', exact: true });
  await detail.getByRole('button', { name: '调整额度', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '调整渠道额度', exact: true });
  await editor.getByLabel('调整数量', { exact: true }).fill('2');
  await editor.getByLabel('调整原因', { exact: true }).fill('恢复丢失的额度响应');
  const attempts: { requestId: string; delta: number; reason: string }[] = [];
  await page.route(`**/api/admin/channels/${channel.id}/quota-adjustments`, async route => {
    attempts.push(route.request().postDataJSON());
    const response = await route.fetch();
    expect(response.ok()).toBe(true);
    if (attempts.length === 1) await route.abort('failed');
    else await route.fulfill({ response });
  });
  await editor.getByRole('button', { name: '确认调整', exact: true }).click();
  await expect(editor.getByText('已保留原调整内容', { exact: true })).toBeVisible();
  await editor.getByRole('button', { name: '关闭', exact: true }).last().click();
  await expect(editor).toBeHidden();
  await detail.getByRole('button', { name: '继续确认', exact: true }).click();
  await expect(editor.getByLabel('调整数量', { exact: true })).toHaveValue('2');
  await expect(editor.getByLabel('调整数量', { exact: true })).toBeDisabled();
  await editor.getByRole('button', { name: '重试原调整', exact: true }).click();
  await expect(editor).toBeHidden();
  await expect(detail.getByTestId('channel-remaining-quota')).toHaveText('5');
  expect(attempts).toHaveLength(2);
  expect(attempts[1]).toEqual(attempts[0]);
  const ledger = await (await page.request.get(`/api/admin/channels/${channel.id}/quota-ledger`)).json();
  expect(ledger.items.filter((item: { reason: string }) => item.reason === '恢复丢失的额度响应')).toHaveLength(1);
});
