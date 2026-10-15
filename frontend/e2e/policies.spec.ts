import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

test.use({ trace: 'off', screenshot: 'off' });
test.afterEach(async ({ page }) => { if (!page.isClosed()) await page.goto('about:blank'); });

async function catalog(page: Page) {
  await page.goto('/login');
  await page.getByLabel('用户名', { exact: true }).fill(process.env.E2E_ADMIN_USER!);
  await page.getByLabel('密码', { exact: true }).fill(process.env.E2E_ADMIN_PASSWORD!);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
  const headers = { 'X-CSRF-TOKEN': (await (await page.request.get('/api/admin/auth/csrf')).json()).token };
  const name = `授权策略-${randomUUID().slice(0, 8)}`;
  const response = await page.request.post('/api/admin/applications', { headers, data: { name, code: `policy-${randomUUID().slice(0, 8)}` } });
  expect(response.status()).toBe(201);
  const app = await response.json();
  const planResponse = await page.request.post(`/api/admin/applications/${app.id}/plans`, { headers, data: {
    name: '策略日卡', durationSeconds: 86400, maxDevices: 3, maxConcurrentSessions: 1, activationValidityDays: 30, features: ['export'],
  } });
  expect(planResponse.status()).toBe(201);
  const plan = await planResponse.json();
  return { app, plan, headers };
}

async function openApplicationSettings(page: Page, name: string) {
  await page.goto('/applications');
  await page.getByRole('row').filter({ hasText: name }).getByRole('button', { name: '应用设置', exact: true }).click();
}

async function issue(page: Page, appId: string, planId: string, headers: Record<string, string>) {
  const response = await page.request.post('/api/admin/license-batches', { headers, data: { applicationId: appId, planId, quantity: 1, requestId: randomUUID() } });
  expect(response.status()).toBe(201);
  return response.json();
}

async function preview(page: Page, name: string) {
  const directory = path.resolve('..', '.local', 'policy-preview');
  await mkdir(directory, { recursive: true });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(page.viewportSize()!.width);
  await page.screenshot({ path: path.join(directory, name), animations: 'disabled' });
}

test('plan editor saves independent online limit, activation days and features', async ({ page }) => {
  const { app, plan } = await catalog(page);
  await page.goto('/plans');
  await page.getByRole('row').filter({ hasText: plan.id.slice(0, 8) }).getByRole('button', { name: '编辑', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '编辑卡种', exact: true });
  await expect(editor.getByLabel('在线会话上限', { exact: true })).toHaveValue('1');
  await editor.getByLabel('在线会话上限', { exact: true }).fill('2');
  await editor.getByLabel('激活期限（天）', { exact: true }).fill('7');
  await editor.getByLabel('授权功能', { exact: true }).fill('reports');
  await editor.getByLabel('授权功能', { exact: true }).press('Enter');
  for (const width of [375, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    await expect(editor.getByRole('button', { name: '保存卡种', exact: true })).toBeInViewport();
    await preview(page, `plan-${width}.png`);
  }
  await editor.getByRole('button', { name: '保存卡种', exact: true }).click();
  await expect(editor).toBeHidden();
  const saved = (await (await page.request.get(`/api/admin/applications/${app.id}/plans`)).json()).items.find((item: { id: string }) => item.id === plan.id);
  expect(saved.maxDevices).toBe(3);
  expect(saved.maxConcurrentSessions).toBe(2);
  expect(saved.activationValidityDays).toBe(7);
  expect(saved.features).toEqual(['export', 'reports']);
});

test('freeze and unfreeze revoke sessions, show history and preserve snapshots on mobile', async ({ page }) => {
  const renderingErrors: string[] = [];
  page.on('pageerror', error => renderingErrors.push(error.message));
  page.on('console', event => { if (event.type() === 'error' && event.text().includes('same key')) renderingErrors.push('Duplicate child key'); });
  const { app, plan, headers } = await catalog(page);
  const issued = await issue(page, app.id, plan.id, headers);
  const card = issued.licenses[0];
  const activation = await page.request.post('/api/client/licenses/activate', { data: { appCode: app.code, licenseKey: card.key, deviceId: randomUUID() } });
  expect(activation.status()).toBe(200);
  const grant = await activation.json();
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto(`/licenses?batchId=${issued.batch.id}`);
  await page.getByRole('row').filter({ hasText: card.keyHint }).getByRole('button', { name: '详情', exact: true }).click();
  const detail = page.getByRole('dialog', { name: '卡密详情', exact: true });
  await detail.getByRole('button', { name: '冻结授权', exact: true }).click();
  const freeze = page.getByRole('dialog', { name: '冻结授权', exact: true });
  await freeze.getByLabel('操作原因', { exact: true }).fill('客户暂时停用');
  await expect(freeze.getByRole('button', { name: '确认冻结', exact: true })).toBeInViewport();
  const freezing = page.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith(`/api/admin/licenses/${card.id}/freeze`));
  await freeze.getByRole('button', { name: '确认冻结', exact: true }).click();
  const freezeResponse = await freezing;
  expect({ status: freezeResponse.status(), code: freezeResponse.ok() ? null : (await freezeResponse.json()).code }).toEqual({ status: 204, code: null });
  await expect(freeze).toBeHidden();
  await expect(detail.getByText('已冻结', { exact: true })).toBeVisible();
  await expect(detail.getByRole('button', { name: '续期卡密', exact: true })).toBeDisabled();
  await expect(detail.getByText('客户暂时停用', { exact: true })).toBeVisible();
  const frozen = (await (await page.request.get(`/api/admin/licenses/${card.id}`)).json()).license;
  expect(frozen.features).toEqual(['export']);
  expect(frozen.maxConcurrentSessions).toBe(1);
  expect(frozen.activateBefore).toBeTruthy();
  const denied = await page.request.post('/api/client/sessions/heartbeat', { headers: { Authorization: `Bearer ${grant.sessionToken}` } });
  expect(denied.status()).toBe(403);
  expect((await denied.json()).code).toBe('LICENSE_FROZEN');
  await preview(page, 'frozen-375.png');
  await detail.getByRole('button', { name: '解冻授权', exact: true }).click();
  const unfreeze = page.getByRole('dialog', { name: '解冻授权', exact: true });
  await unfreeze.getByLabel('操作原因', { exact: true }).fill('客户恢复使用');
  const unfreezing = page.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith(`/api/admin/licenses/${card.id}/unfreeze`));
  await unfreeze.getByRole('button', { name: '确认解冻', exact: true }).click();
  const unfreezeResponse = await unfreezing;
  expect({ status: unfreezeResponse.status(), code: unfreezeResponse.ok() ? null : (await unfreezeResponse.json()).code }).toEqual({ status: 204, code: null });
  await expect(unfreeze).toBeHidden();
  await expect(detail.getByText('有效', { exact: true })).toBeVisible();
  await expect(detail.getByText('客户恢复使用', { exact: true })).toBeVisible();
  await preview(page, 'unfrozen-375.png');
  expect(renderingErrors).toEqual([]);
});

test('trial policy enables a single-device trial and paid activation upgrades it', async ({ page }) => {
  const { app, plan, headers } = await catalog(page);
  await openApplicationSettings(page, app.name);
  await page.getByRole('button', { name: '配置试用', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '试用授权配置', exact: true });
  await editor.getByRole('switch', { name: '允许领取试用', exact: true }).check();
  await editor.getByLabel('试用时长（小时）', { exact: true }).fill('1');
  await editor.getByLabel('试用功能', { exact: true }).fill('preview');
  await editor.getByLabel('试用功能', { exact: true }).press('Enter');
  await page.setViewportSize({ width: 375, height: 812 });
  await preview(page, 'trial-policy-375.png');
  await editor.getByRole('button', { name: '保存试用规则', exact: true }).click();
  await expect(editor).toBeHidden();
  const deviceId = randomUUID();
  const claim = async () => page.request.post('/api/client/trials/activate', { data: { appCode: app.code, deviceId } });
  const first = await claim();
  expect(first.status()).toBe(200);
  const trial = await first.json();
  expect(trial.isTrial).toBe(true);
  expect(trial.features).toEqual(['preview']);
  const retry = await claim();
  expect(retry.status()).toBe(200);
  const retried = await retry.json();
  expect(retried.licenseId).toBe(trial.licenseId);
  expect(retried.expiresAt).toBe(trial.expiresAt);
  const issued = await issue(page, app.id, plan.id, headers);
  const paid = await page.request.post('/api/client/licenses/activate', { data: { appCode: app.code, licenseKey: issued.licenses[0].key, deviceId } });
  expect(paid.status()).toBe(200);
  const upgraded = await paid.json();
  expect(upgraded.isTrial).toBe(false);
  expect(upgraded.features).toEqual(['export']);
  expect((await claim()).status()).toBe(403);
  expect((await page.request.post('/api/client/sessions/heartbeat', { headers: { Authorization: `Bearer ${retried.sessionToken}` } })).ok()).toBe(false);
});
