import { test, expect } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { catalog } from './roadmap-helpers';

test.use({ trace: 'off', screenshot: 'off' });
// Clear credential views before Playwright creates any failure page context.
test.afterEach(async ({ page }) => { if (!page.isClosed()) await page.goto('about:blank'); });

test('webhook administration creates a disabled subscription and rotates its secret', async ({ page }) => {
  const { app } = await catalog(page, 1);
  await page.goto('/integrations');
  await expect(page.getByRole('heading', { name: '开放集成', exact: true })).toBeVisible();
  await page.getByRole('tab', { name: 'Webhook', exact: true }).click();
  await page.getByRole('button', { name: '创建订阅', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '创建 Webhook 订阅', exact: true });
  await editor.getByLabel('所属应用', { exact: true }).fill(app.name);
  await page.getByTitle(app.name, { exact: true }).click();
  const name = `本机订阅-${randomUUID().slice(0, 8)}`;
  await editor.getByLabel('订阅名称', { exact: true }).fill(name);
  await editor.getByLabel('接收地址', { exact: true }).fill('http://127.0.0.1:5091/events');
  await editor.getByRole('checkbox', { name: '授权到期提醒', exact: true }).check();
  if (await editor.getByRole('switch', { name: '启用订阅', exact: true }).getAttribute('aria-checked') === 'true') await editor.getByRole('switch', { name: '启用订阅', exact: true }).click();
  await editor.getByRole('button', { name: '保存订阅', exact: true }).click();
  const secretDialog = page.getByRole('dialog', { name: '保存 Webhook 签名密钥', exact: true });
  await expect(secretDialog).toBeVisible();
  const original = (await secretDialog.getByTestId('one-time-secret').textContent())!;
  expect(/^[A-Za-z0-9+/]{43}=$/.test(original)).toBe(true);
  await secretDialog.getByRole('button', { name: '已保存，关闭', exact: true }).click();
  const row = page.getByRole('row').filter({ hasText: name });
  await expect(row).toContainText('已停用');
  await row.getByRole('button', { name: '轮换密钥', exact: true }).click();
  await page.getByRole('button', { name: '确认轮换', exact: true }).click();
  await expect(secretDialog).toBeVisible();
  const rotated = (await secretDialog.getByTestId('one-time-secret').textContent())!;
  expect(rotated !== original && /^[A-Za-z0-9+/]{43}=$/.test(rotated)).toBe(true);
  await secretDialog.getByRole('button', { name: '已保存，关闭', exact: true }).click();
  expect((await page.locator('body').textContent())!.includes(rotated)).toBe(false);
});
