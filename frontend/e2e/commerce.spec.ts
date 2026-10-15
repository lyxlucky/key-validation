import { test, expect } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { catalog, login } from './roadmap-helpers';

test.use({ trace: 'off', screenshot: 'off' });
test.afterEach(async ({ page }) => { await page.goto('about:blank'); });

test('customer records can be created, edited and found after a page reload', async ({ page }) => {
  const { app } = await catalog(page, 1);
  const customerName = `客户验收-${randomUUID().slice(0, 8)}`;
  await page.goto('/customers');
  await expect(page.getByRole('heading', { name: '客户管理', exact: true })).toBeVisible();
  await page.getByRole('button', { name: '新建客户', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '新建客户', exact: true });
  await editor.getByLabel('客户应用', { exact: true }).fill(app.name);
  await page.getByTitle(app.name, { exact: true }).click();
  await editor.getByLabel('客户名称', { exact: true }).fill(customerName);
  await editor.getByLabel('备注', { exact: true }).fill('人工交付测试客户');
  await editor.getByRole('button', { name: '保存客户', exact: true }).click();
  await expect(editor).toBeHidden();
  await page.getByLabel('搜索客户', { exact: true }).fill(customerName);
  const row = page.getByRole('row').filter({ hasText: customerName });
  await row.getByRole('button', { name: '编辑', exact: true }).click();
  const editing = page.getByRole('dialog', { name: '编辑客户', exact: true });
  await editing.getByLabel('备注', { exact: true }).fill('已核对客户交付信息');
  await editing.getByRole('button', { name: '保存客户', exact: true }).click();
  await expect(editing).toBeHidden();
  await page.reload();
  await page.getByLabel('搜索客户', { exact: true }).fill(customerName);
  await expect(page.getByRole('row').filter({ hasText: customerName })).toBeVisible();
  await page.getByRole('row').filter({ hasText: customerName }).getByRole('button', { name: '编辑', exact: true }).click();
  await expect(page.getByRole('dialog', { name: '编辑客户', exact: true }).getByLabel('备注', { exact: true })).toHaveValue('已核对客户交付信息');
});

test('customer portal opens without using the existing administrator session or its API', async ({ page }) => {
  await login(page);
  await page.waitForLoadState('networkidle');
  const adminRequests: string[] = [];
  page.on('request', request => { if (new URL(request.url()).pathname.startsWith('/api/admin/')) adminRequests.push(request.method()); });
  await page.goto('/portal');
  await expect(page.getByRole('heading', { name: '客户自助', exact: true })).toBeVisible();
  await expect(page.getByLabel('客户访问码', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: '进入自助服务', exact: true })).toBeVisible();
  await expect(page.getByRole('navigation', { name: '主导航', exact: true })).toHaveCount(0);
  expect(adminRequests).toEqual([]);
});
