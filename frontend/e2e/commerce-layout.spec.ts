import { test, expect } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import { login } from './roadmap-helpers';

test.use({ trace: 'off', screenshot: 'off' });
test.afterEach(async ({ page }) => { if (!page.isClosed()) await page.goto('about:blank'); });

test('commerce pages and editors fit phone, tablet and desktop viewports', async ({ page }) => {
  test.setTimeout(180000);
  const directory = path.resolve('..', '.local', 'roadmap-preview');
  await mkdir(directory, { recursive: true });
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await login(page);
  const pages = [
    { route: 'customers', title: '客户管理', create: '新建客户', save: '保存客户' },
    { route: 'orders', title: '订单管理', create: '新建订单', save: '保存订单' },
    { route: 'channels', title: '渠道管理', create: '新建渠道', save: '保存渠道' },
    { route: 'portal-requests', title: '客户申请' },
  ];
  for (const width of [375, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 800 });
    for (const item of pages) {
      await page.goto(`/${item.route}`);
      await expect(page.getByRole('heading', { name: item.title, exact: true })).toBeVisible();
      await expect(page.locator('.ant-spin-spinning:visible')).toHaveCount(0);
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth), { message: `${item.route} at ${width}px` }).toBeLessThanOrEqual(width);
      if (width === 375) {
        const actionWidth = (await page.getByRole('columnheader', { name: '操作', exact: true }).boundingBox())!.width;
        const tableWidth = (await page.locator('.ant-table-content:visible').first().boundingBox())!.width;
        expect(actionWidth).toBeLessThan(tableWidth / 2);
      }
      await page.screenshot({ path: path.join(directory, `${item.route}-${width}.png`), fullPage: true, animations: 'disabled' });
      if (item.create && (width === 375 || width === 1440)) {
        await page.getByRole('button', { name: item.create, exact: true }).click();
        const editor = page.getByRole('dialog', { name: item.create, exact: true });
        await expect(editor).toBeVisible();
        await expect.poll(async () => { const box = await editor.getByRole('button', { name: item.save!, exact: true }).boundingBox(); return box ? box.y + box.height : Infinity; }).toBeLessThanOrEqual(800);
        await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
        await page.screenshot({ path: path.join(directory, `${item.route}-editor-${width}.png`), fullPage: true, animations: 'disabled' });
        await editor.getByRole('button', { name: '关闭', exact: true }).first().click();
        await expect(editor).toBeHidden();
      }
    }
    await page.goto('/integrations');
    await page.getByRole('tab', { name: 'API 凭证', exact: true }).click();
    await expect(page.locator('.ant-spin-spinning:visible')).toHaveCount(0);
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await page.screenshot({ path: path.join(directory, `api-credentials-${width}.png`), fullPage: true, animations: 'disabled' });
  }
  expect(errors).toEqual([]);
});
