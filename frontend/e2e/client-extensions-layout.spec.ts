import { test, expect } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import { catalog } from './roadmap-helpers';

test.use({ trace: 'off', screenshot: 'off' });
test.afterEach(async ({ page }) => { if (!page.isClosed()) await page.goto('about:blank'); });

test('version and offline authorization forms fit mobile, tablet and desktop screens', async ({ page }) => {
  test.setTimeout(180000);
  const { app } = await catalog(page, 1);
  const directory = path.resolve('..', '.local', 'roadmap-preview');
  await mkdir(directory, { recursive: true });
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  for (const width of [375, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 800 });
    await page.goto('/client-policies');
    await expect(page.getByRole('heading', { name: '客户端版本', exact: true })).toBeVisible();
    await page.getByLabel('版本策略应用', { exact: true }).fill(app.name);
    await page.getByTitle(app.name, { exact: true }).click();
    await expect(page.getByLabel('最低支持版本', { exact: true })).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await page.screenshot({ path: path.join(directory, 'client-policies-top-' + width + '.png'), fullPage: true, animations: 'disabled' });
    const save = page.getByRole('button', { name: '保存版本策略', exact: true });
    await save.scrollIntoViewIfNeeded();
    await expect(save).toBeInViewport();
    await page.screenshot({ path: path.join(directory, `client-policies-${width}.png`), fullPage: true, animations: 'disabled' });
    await page.goto('/offline-licenses');
    await expect(page.getByRole('heading', { name: '离线授权', exact: true })).toBeVisible();
    await expect(page.locator('.ant-spin-spinning:visible')).toHaveCount(0);
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await page.screenshot({ path: path.join(directory, `offline-licenses-${width}.png`), fullPage: true, animations: 'disabled' });
    await page.getByRole('button', { name: '签发离线授权', exact: true }).click();
    const sign = page.getByRole('dialog', { name: '签发离线授权', exact: true });
    await expect(sign).toBeVisible();
    await expect.poll(async () => { const box = await sign.getByRole('button', { name: '确认签发', exact: true }).boundingBox(); return box ? box.y + box.height : Infinity; }).toBeLessThanOrEqual(800);
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    // The screenshot contains an empty form, never a device ID or certificate.
    await page.screenshot({ path: path.join(directory, `offline-sign-${width}.png`), fullPage: true, animations: 'disabled' });
    await sign.getByRole('button', { name: '取消', exact: true }).click();
  }
  expect(errors).toEqual([]);
});
