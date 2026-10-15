import { test, expect } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { catalog, login } from './roadmap-helpers';

test.use({ trace: 'off', screenshot: 'off' });

test('risk policy detects repeated activation and records a review', async ({ page }) => {
  const { app, issuance } = await catalog(page, 1);
  await page.goto('/risk');
  await expect(page.getByRole('heading', { name: '异常检测', exact: true })).toBeVisible();
  await page.getByRole('button', { name: '风险策略', exact: true }).click();
  const policy = page.getByRole('dialog', { name: '风险策略', exact: true });
  await policy.getByLabel('策略应用', { exact: true }).fill(app.name);
  await page.getByTitle(app.name, { exact: true }).click();
  const riskEnabled = policy.getByRole('switch', { name: '记录异常', exact: true });
  if (await riskEnabled.getAttribute('aria-checked') !== 'true') await riskEnabled.click();
  await policy.getByLabel('激活次数阈值', { exact: true }).fill('2');
  await policy.getByRole('button', { name: '保存策略', exact: true }).click();
  await expect(policy).toBeHidden();
  const deviceId = randomUUID();
  for (let index = 0; index < 2; index++) {
    expect((await page.request.post('/api/client/licenses/activate', { data: { appCode: app.code, licenseKey: issuance.licenses[0].key, deviceId } })).status()).toBe(200);
  }
  await page.getByRole('button', { name: '立即扫描', exact: true }).click();
  await page.getByLabel('风险应用', { exact: true }).fill(app.name);
  await page.getByTitle(app.name, { exact: true }).click();
  await page.getByRole('row').filter({ hasText: app.name }).getByRole('button', { name: '复核', exact: true }).first().click();
  const review = page.getByRole('dialog', { name: '复核异常', exact: true });
  await review.getByRole('radio', { name: '确认异常', exact: true }).check();
  await review.getByLabel('复核原因', { exact: true }).fill('已核对测试设备的重复激活记录');
  await review.getByRole('button', { name: '保存复核', exact: true }).click();
  await expect(review).toBeHidden();
  await expect(page.getByRole('row').filter({ hasText: app.name }).first()).toContainText('已确认');
});

test('notification policy delivers an expiry notice and supports independent read state', async ({ page }) => {
  const { app, issuance } = await catalog(page, 1);
  await page.goto('/notifications');
  await expect(page.getByRole('heading', { name: '通知中心', exact: true })).toBeVisible();
  await page.getByRole('button', { name: '通知设置', exact: true }).click();
  const policy = page.getByRole('dialog', { name: '通知设置', exact: true });
  await policy.getByLabel('通知应用', { exact: true }).fill(app.name);
  await page.getByTitle(app.name, { exact: true }).click();
  const notificationsEnabled = policy.getByRole('switch', { name: '启用通知', exact: true });
  if (await notificationsEnabled.getAttribute('aria-checked') !== 'true') await notificationsEnabled.click();
  await policy.getByLabel('接收账号', { exact: true }).fill(process.env.E2E_ADMIN_USER!);
  await page.getByTitle(process.env.E2E_ADMIN_USER!, { exact: true }).click();
  await policy.getByLabel('接收账号', { exact: true }).press('Escape');
  await policy.getByRole('checkbox', { name: '授权到期提醒', exact: true }).check();
  await policy.getByRole('checkbox', { name: '3 天', exact: true }).check();
  await policy.getByRole('button', { name: '保存设置', exact: true }).click();
  await expect(policy).toBeHidden();
  expect((await page.request.post('/api/client/licenses/activate', { data: { appCode: app.code, licenseKey: issuance.licenses[0].key, deviceId: randomUUID() } })).status()).toBe(200);
  const headers = { 'X-CSRF-TOKEN': (await (await page.request.get('/api/admin/auth/csrf')).json()).token };
  expect((await page.request.post('/api/admin/operations/scan', { headers })).ok()).toBe(true);
  await page.getByRole('button', { name: '刷新通知', exact: true }).click();
  await page.getByLabel('筛选通知应用', { exact: true }).fill(app.name);
  await page.getByTitle(app.name, { exact: true }).click();
  const row = page.getByRole('row').filter({ hasText: app.name }).first();
  await expect(row).toContainText('到期');
  await row.getByRole('button', { name: '标为已读', exact: true }).click();
  await expect(row).toContainText('已读');
});

test('operations console samples monitoring and verifies a real encrypted backup', async ({ page }) => {
  test.setTimeout(120000);
  await login(page);
  await page.goto('/operations');
  await expect(page.getByRole('heading', { name: '运维中心', exact: true })).toBeVisible();
  await page.getByRole('button', { name: '立即采样', exact: true }).click();
  await expect(page.getByTestId('database-status')).toContainText('可用');
  await page.getByRole('tab', { name: '备份与恢复', exact: true }).click();
  const created = page.waitForResponse(response => response.url().endsWith('/api/admin/backups') && response.request().method() === 'POST');
  await page.getByRole('button', { name: '创建备份', exact: true }).click();
  const backupResponse = await created;
  expect(backupResponse.ok()).toBe(true);
  const backup = await backupResponse.json();
  const row = page.locator(`tr[data-row-key="${backup.id}"]`);
  await expect(row).toContainText('成功');
  await row.getByRole('button', { name: '验证恢复', exact: true }).click();
  await page.getByRole('button', { name: '开始验证', exact: true }).click();
  await expect(row).toContainText('已验证', { timeout: 60000 });
});
