import { test, expect } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import { login } from './roadmap-helpers';

test.use({ trace: 'off', screenshot: 'off' });

test('roadmap pages and advanced filters fit all supported viewport widths', async ({ page }) => {
  test.setTimeout(240000);
  const directory = path.resolve('..', '.local', 'roadmap-preview');
  await mkdir(directory, { recursive: true });
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await login(page);
  for (const width of [375, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 960 });
    for (const [route, title] of [['reports', '运营统计'], ['risk', '异常检测'], ['notifications', '通知中心'], ['operations', '运维中心'], ['integrations', '开放集成']]) {
      await page.goto(`/${route}`);
      await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
      await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
      await page.evaluate(() => document.fonts.ready);
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth), { message: `${route} must fit ${width}px` }).toBeLessThanOrEqual(width);
      if (route === 'integrations') {
        await page.screenshot({ path: path.join(directory, `integration-guide-${width}.png`), fullPage: true, animations: 'disabled' });
        await page.getByRole('tab', { name: 'Webhook', exact: true }).click();
        await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
        await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      }
      if (route === 'integrations' && width === 375) {
        const actions = page.getByRole('columnheader', { name: '操作', exact: true });
        const actionWidth = (await actions.boundingBox())!.width;
        const tableWidth = (await page.locator('.ant-table-content').filter({ visible: true }).first().boundingBox())!.width;
        expect(actionWidth, 'fixed actions must leave room to identify the subscription').toBeLessThan(tableWidth / 2);
      }
      if (route === 'reports') {
        const renderedAxisFont = await page.locator('.operations-chart text').first().evaluate(element => {
          const scale = (element as SVGGraphicsElement).getScreenCTM()?.a ?? 0;
          return parseFloat(getComputedStyle(element).fontSize) * scale;
        });
        expect(renderedAxisFont).toBeGreaterThanOrEqual(11);
      }
      await page.screenshot({ path: path.join(directory, `${route}-${width}.png`), fullPage: true, animations: 'disabled' });
      if (route === 'operations') {
        await page.getByRole('tab', { name: '备份与恢复', exact: true }).click();
        await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
        await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
        await page.screenshot({ path: path.join(directory, `backups-${width}.png`), fullPage: true, animations: 'disabled' });
      }
    }
    await page.goto('/licenses');
    await page.getByRole('button', { name: '高级筛选', exact: true }).click();
    await expect(page.getByLabel('到期结束日', { exact: true })).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await page.screenshot({ path: path.join(directory, `advanced-search-${width}.png`), fullPage: true, animations: 'disabled' });
  }
  expect(errors).toEqual([]);
});
