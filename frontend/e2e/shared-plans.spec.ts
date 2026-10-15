import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { syntheticAdmin } from './review-lifecycle-helpers';

test.use({ trace: 'off', screenshot: 'off' });

async function loginSharedPlans(page: Page) {
  await page.goto('/login');
  await page.getByLabel('用户名', { exact: true }).fill(process.env.E2E_ADMIN_USER!);
  await page.getByLabel('密码', { exact: true }).fill(process.env.E2E_ADMIN_PASSWORD!);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
  const csrf = await (await page.request.get('/api/admin/auth/csrf')).json();
  return { 'X-CSRF-TOKEN': csrf.token as string };
}

test('global plan page creates and edits complete shared rules', async ({ page }) => {
  await loginSharedPlans(page);
  await page.goto('/plans');
  await expect(page.getByRole('heading', { name: '卡种管理', exact: true })).toBeVisible();
  const name = '共享月卡-' + randomUUID().slice(0, 8);
  await page.getByRole('button', { name: '创建卡种', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '创建卡种', exact: true });
  await editor.getByLabel('卡种名称', { exact: true }).fill(name);
  await editor.getByLabel('时长单位', { exact: true }).click();
  await page.getByTitle('小时', { exact: true }).click();
  await editor.getByLabel('授权时长', { exact: true }).fill('720');
  await editor.getByLabel('设备上限', { exact: true }).fill('3');
  await editor.getByLabel('在线会话上限', { exact: true }).fill('2');
  await editor.getByLabel('激活期限（天）', { exact: true }).fill('30');
  await editor.getByLabel('授权功能', { exact: true }).fill('export');
  await editor.getByLabel('授权功能', { exact: true }).press('Enter');
  for (const width of [375, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    await expect(editor.getByRole('button', { name: '保存卡种', exact: true })).toBeInViewport();
  }
  await editor.getByRole('button', { name: '保存卡种', exact: true }).click();
  await expect(editor).toBeHidden();
  const row = page.getByRole('row').filter({ hasText: name });
  await expect(row).toContainText('0 个应用');
  await row.getByRole('button', { name: '编辑', exact: true }).click();
  const editing = page.getByRole('dialog', { name: '编辑卡种', exact: true });
  await editing.getByLabel('在线会话上限', { exact: true }).fill('1');
  await editing.getByRole('button', { name: '保存卡种', exact: true }).click();
  await expect(editing).toBeHidden();
  const saved = await (await page.request.get(
    '/api/admin/plans?search=' + encodeURIComponent(name))).json();
  expect(saved.items).toHaveLength(1);
  expect(saved.items[0].maxDevices).toBe(3);
  expect(saved.items[0].maxConcurrentSessions).toBe(1);
  expect(saved.items[0].features).toEqual(['export']);
  await row.getByRole('button', { name: '编辑', exact: true }).click();
  const statusEditor = page.getByRole('dialog', { name: '编辑卡种', exact: true });
  await statusEditor.getByLabel('允许继续发行', { exact: true }).click();
  await statusEditor.getByRole('button', { name: '保存卡种', exact: true }).click();
  await expect(statusEditor).toBeHidden();
  await page.getByLabel('卡种发行状态', { exact: true }).click();
  await page.getByTitle('已停发', { exact: true }).click();
  await expect(page.getByRole('row').filter({ hasText: name })).toBeVisible();
  await page.getByLabel('卡种发行状态', { exact: true }).click();
  await page.getByTitle('可发行', { exact: true }).click();
  await expect(page.getByRole('row').filter({ hasText: name })).toHaveCount(0);
});

test('applications reference one global plan and retain trial settings', async ({ page }) => {
  const headers = await loginSharedPlans(page);
  const suffix = randomUUID().slice(0, 8);
  const created = await page.request.post('/api/admin/plans', {
    headers, data: { name: '共用年卡-' + suffix, durationSeconds: 31536000, maxDevices: 2 },
  });
  expect(created.status()).toBe(201);
  const plan = await created.json();
  const applications: { id: string; name: string }[] = [];
  for (const letter of ['a', 'b']) {
    const response = await page.request.post('/api/admin/applications', {
      headers, data: { code: 'shared-' + letter + '-' + suffix,
        name: '引用应用-' + letter + '-' + suffix },
    });
    expect(response.status()).toBe(201);
    applications.push(await response.json());
  }
  await page.goto('/applications');
  for (const app of applications) {
    await page.getByRole('row').filter({ hasText: app.name })
      .getByRole('button', { name: '应用设置', exact: true }).click();
    const settings = page.getByRole('dialog', { name: app.name + ' · 应用设置', exact: true });
    await expect(settings.getByRole('heading', { name: '试用授权', exact: true })).toBeVisible();
    await settings.getByLabel('选择全局卡种', { exact: true }).click();
    await page.getByTitle(plan.name + ' · ' + plan.id.slice(0, 8), { exact: true }).click();
    await settings.getByRole('button', { name: '引用卡种', exact: true }).click();
    await expect(settings.getByRole('row').filter({ hasText: plan.name })).toBeVisible();
    await settings.getByRole('button', { name: '关闭', exact: true }).click();
  }
  const global = await (await page.request.get('/api/admin/plans/' + plan.id)).json();
  expect(global.referenceCount).toBe(2);
  for (const app of applications) {
    const scoped = await (await page.request.get('/api/admin/applications/' + app.id + '/plans')).json();
    expect(scoped.items.find((item: { id: string }) => item.id === plan.id).applicationId).toBe(app.id);
  }
});

async function createSharedBrowserCatalog(page: Page, headers: Record<string, string>) {
  const suffix = randomUUID().slice(0, 8);
  const created = await page.request.post('/api/admin/plans', {
    headers, data: { name: '公共卡-' + suffix, durationSeconds: 86400, maxDevices: 1 },
  });
  expect(created.status()).toBe(201);
  const plan = await created.json();
  const applications: { id: string; name: string; code: string }[] = [];
  for (const letter of ['a', 'b']) {
    const response = await page.request.post('/api/admin/applications', {
      headers, data: { code: 'catalog-' + letter + '-' + suffix,
        name: '共用应用-' + letter + '-' + suffix },
    });
    expect(response.status()).toBe(201);
    const app = await response.json();
    applications.push(app);
    const bound = await page.request.put(
      '/api/admin/applications/' + app.id + '/plan-references/' + plan.id,
      { headers, data: { isEnabled: true } });
    expect(bound.status()).toBe(200);
  }
  return { plan, applications, suffix };
}

test('channel price editor distinguishes applications sharing a plan', async ({ page }) => {
  const headers = await loginSharedPlans(page);
  const { plan, applications, suffix } = await createSharedBrowserCatalog(page, headers);
  const [a, b] = applications;
  const accountResponse = await page.request.post('/api/admin/accounts', {
    headers, data: { userName: 'plan-channel-' + suffix,
      password: 'Aa1!' + randomUUID().replaceAll('-', ''), role: 'Operator' },
  });
  expect(accountResponse.status()).toBe(201);
  const account = await accountResponse.json();
  const channelName = '共用定价渠道-' + suffix;
  const channelResponse = await page.request.post('/api/admin/channels', {
    headers, data: { name: channelName, accountId: account.id,
      applicationIds: [a.id, b.id], initialQuota: 10 },
  });
  expect(channelResponse.status()).toBe(201);
  const channel = await channelResponse.json();
  for (const [applicationId, unitPriceCents] of [[a.id, 1100], [b.id, 2200]]) {
    const saved = await page.request.put('/api/admin/channels/' + channel.id + '/prices',
      { headers, data: { applicationId, planId: plan.id, unitPriceCents } });
    expect(saved.status()).toBe(200);
  }
  await page.goto('/channels');
  await page.getByLabel('搜索渠道', { exact: true }).fill(channelName);
  await page.getByRole('row').filter({ hasText: channelName })
    .getByRole('button', { name: '详情', exact: true }).click();
  await page.getByRole('button', { name: '设置售价', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '设置渠道售价', exact: true });
  await editor.getByLabel('定价应用', { exact: true }).click();
  await page.getByTitle(b.name, { exact: true }).click();
  await editor.getByLabel('定价卡种', { exact: true }).click();
  await page.getByTitle(plan.name, { exact: true }).click();
  await expect(editor.getByLabel('售价（分）', { exact: true })).toHaveValue('2200');
  await editor.getByLabel('售价（分）', { exact: true }).fill('3300');
  await editor.getByRole('button', { name: '保存售价', exact: true }).click();
  await expect(editor).toBeHidden();
  const saved = await (await page.request.get('/api/admin/channels/' + channel.id)).json();
  expect(saved.prices.find((x: { applicationId: string }) => x.applicationId === a.id).unitPriceCents).toBe(1100);
  expect(saved.prices.find((x: { applicationId: string }) => x.applicationId === b.id).unitPriceCents).toBe(3300);
});

for (const recovery of [false, true]) {
  test('reference rejection ' + (recovery ? 'preserves uncertain request' : 'allows a new selection'),
    async ({ page }) => {
      const headers = await loginSharedPlans(page);
      const { plan, applications } = await createSharedBrowserCatalog(page, headers);
      const app = applications[0];
      await page.goto('/licenses');
      await page.getByRole('button', { name: /生成卡密$/ }).click();
      const modal = page.getByRole('dialog', { name: '生成卡密', exact: true });
      await modal.getByLabel('所属应用', { exact: true }).click();
      await page.getByTitle(app.name, { exact: true }).click();
      await modal.getByLabel('卡种', { exact: true }).click();
      await page.getByTitle(plan.name, { exact: true }).click();
      let attempt = 0;
      const requests: unknown[] = [];
      await page.route('**/api/admin/license-batches', async route => {
        if (route.request().method() !== 'POST') return route.continue();
        requests.push(route.request().postDataJSON());
        attempt++;
        const uncertain = recovery && attempt === 1;
        await route.fulfill({
          status: uncertain ? 503 : 409, contentType: 'application/json',
          body: JSON.stringify({ code: uncertain ? 'INTERNAL_ERROR' : 'APPLICATION_PLAN_DISABLED' }),
        });
      });
      await modal.getByRole('button', { name: '确认生成', exact: true }).click();
      if (recovery) {
        await expect(modal.getByText('存在待确认的发卡请求', { exact: true })).toBeVisible();
        await modal.getByRole('button', { name: '使用原请求重试', exact: true }).click();
        await expect(modal.getByText('该应用已暂停此卡种发行，请选择其他卡种', { exact: true })).toBeVisible();
        await expect(modal.getByText('存在待确认的发卡请求', { exact: true })).toBeVisible();
        expect(requests).toHaveLength(2);
        expect(requests[1]).toEqual(requests[0]);
      } else {
        await expect(modal.getByText('该应用已暂停此卡种发行，请选择其他卡种', { exact: true })).toBeVisible();
        await expect(modal.getByText('存在待确认的发卡请求', { exact: true })).toHaveCount(0);
        await expect(modal.getByRole('button', { name: '确认生成', exact: true })).toBeVisible();
      }
    });
}

test('shared edits refresh existing pages while paused references remain in history', async ({ page }) => {
  test.setTimeout(90000);
  const headers = await loginSharedPlans(page);
  const { plan, applications } = await createSharedBrowserCatalog(page, headers);
  const [a, b] = applications;
  const originalResponse = await page.request.post('/api/admin/license-batches', {
    headers, data: { applicationId: a.id, planId: plan.id, quantity: 1, requestId: randomUUID() },
  });
  expect(originalResponse.status()).toBe(201);
  const original = await originalResponse.json();

  await page.goto('/licenses');
  await page.getByRole('button', { name: /生成卡密$/ }).click();
  let modal = page.getByRole('dialog', { name: '生成卡密', exact: true });
  await modal.getByLabel('所属应用', { exact: true }).click();
  await page.getByTitle(a.name, { exact: true }).click();
  await modal.getByLabel('卡种', { exact: true }).click();
  await page.getByTitle(plan.name, { exact: true }).click();
  await modal.getByRole('button', { name: '取消', exact: true }).click();

  await page.getByRole('menuitem', { name: '卡种管理', exact: true }).click();
  await page.getByRole('row').filter({ hasText: plan.id.slice(0, 8) })
    .getByRole('button', { name: '编辑', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '编辑卡种', exact: true });
  const changedName = plan.name + '-新版';
  await editor.getByLabel('卡种名称', { exact: true }).fill(changedName);
  await editor.getByLabel('时长单位', { exact: true }).click();
  await page.getByTitle('小时', { exact: true }).click();
  await editor.getByLabel('授权时长', { exact: true }).fill('2');
  await editor.getByRole('button', { name: '保存卡种', exact: true }).click();
  await expect(editor).toBeHidden();

  await page.getByRole('menuitem', { name: '卡密管理', exact: true }).click();
  await page.getByRole('button', { name: /生成卡密$/ }).click();
  modal = page.getByRole('dialog', { name: '生成卡密', exact: true });
  await modal.getByLabel('所属应用', { exact: true }).click();
  await page.getByTitle(b.name, { exact: true }).click();
  await modal.getByLabel('卡种', { exact: true }).click();
  await page.getByTitle(changedName, { exact: true }).click();
  await modal.getByRole('button', { name: '取消', exact: true }).click();
  const newResponse = await page.request.post('/api/admin/license-batches', {
    headers, data: { applicationId: b.id, planId: plan.id, quantity: 1, requestId: randomUUID() },
  });
  expect(newResponse.status()).toBe(201);
  const issued = await newResponse.json();
  const oldDetail = await (await page.request.get(
    '/api/admin/licenses/' + original.licenses[0].id)).json();
  const newDetail = await (await page.request.get(
    '/api/admin/licenses/' + issued.licenses[0].id)).json();
  expect(oldDetail.license.durationSeconds).toBe(86400);
  expect(newDetail.license.durationSeconds).toBe(7200);

  await page.getByRole('menuitem', { name: '应用管理', exact: true }).click();
  await page.getByRole('row').filter({ hasText: a.name })
    .getByRole('button', { name: '应用设置', exact: true }).click();
  const settings = page.getByRole('dialog', { name: a.name + ' · 应用设置', exact: true });
  const toggle = settings.getByRole('switch', { name: '允许本应用发行 ' + changedName, exact: true });
  await toggle.click();
  await expect(toggle).not.toBeChecked();
  await settings.getByRole('button', { name: '关闭', exact: true }).click();

  await page.getByRole('menuitem', { name: '卡密管理', exact: true }).click();
  await page.getByRole('button', { name: /生成卡密$/ }).click();
  modal = page.getByRole('dialog', { name: '生成卡密', exact: true });
  await modal.getByLabel('所属应用', { exact: true }).click();
  await page.getByTitle(a.name, { exact: true }).click();
  await expect(modal.getByText(
    '该应用暂无可发行卡种，请在应用设置中引用卡种并启用发行；新卡种在卡种管理中创建。', { exact: true })).toBeVisible();
  await modal.getByRole('button', { name: '取消', exact: true }).click();
  await page.getByRole('menuitem', { name: '批次管理', exact: true }).click();
  await page.locator('#batch-application-filter').click();
  await page.getByTitle(a.name, { exact: true }).click();
  await page.locator('#batch-plan-filter').click();
  await expect(page.getByTitle(changedName, { exact: true })).toBeVisible();
});

test('global plan draft survives tab hiding and resets after cancellation', async ({ page }) => {
  await loginSharedPlans(page);
  await page.getByRole('menuitem', { name: '授权管理', exact: true }).click();
  await page.getByRole('menuitem', { name: '卡种管理', exact: true }).click();
  await page.getByRole('button', { name: '创建卡种', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '创建卡种', exact: true });
  await editor.getByLabel('卡种名称', { exact: true }).fill('未提交的共用卡种');
  await editor.getByLabel('设备上限', { exact: true }).fill('7');
  await page.goBack();
  await expect(editor).toBeHidden();
  await page.getByRole('tab', { name: '卡种管理', exact: true }).click();
  await expect(editor.getByLabel('卡种名称', { exact: true })).toHaveValue('未提交的共用卡种');
  await expect(editor.getByLabel('设备上限', { exact: true })).toHaveValue('7');
  await editor.getByRole('button', { name: '取消', exact: true }).click();
  await expect(editor).toBeHidden();
  await page.getByRole('button', { name: '创建卡种', exact: true }).click();
  await expect(editor.getByLabel('卡种名称', { exact: true })).toHaveValue('');
  await expect(editor.getByLabel('设备上限', { exact: true })).toHaveValue('1');
});

test('scoped platform manager cannot enter global plan management', async ({ page }) => {
  await syntheticAdmin(page, { permissions: ['platform.manage', 'licenses.read'] });
  await page.route('**/api/admin/auth/access', route => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({ permissions: ['platform.manage', 'licenses.read'],
      allApplications: false, applicationIds: ['review-application'], channelId: null }),
  }));
  const globalRequests: string[] = [];
  page.on('request', request => {
    if (new URL(request.url()).pathname === '/api/admin/plans') globalRequests.push(request.method());
  });
  await page.goto('/plans');
  await expect(page.getByText('没有访问权限', { exact: true })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: '卡种管理', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: '创建卡种', exact: true })).toHaveCount(0);
  expect(globalRequests).toEqual([]);
});
