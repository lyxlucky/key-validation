import { test, expect } from '@playwright/test';
import { login } from './roadmap-helpers';

test.use({ trace: 'off', screenshot: 'off' });

test('expanded roadmap navigation keeps its last destination reachable on short screens', async ({ page }) => {
  await login(page);
  for (const width of [1440, 768, 375]) {
    await page.setViewportSize({ width, height: 640 });
    await page.goto('/overview');
    if (width < 768) await page.getByRole('button', { name: '打开导航', exact: true }).click();
    const navigation = page.getByRole('navigation', { name: '主导航', exact: true }).filter({ visible: true });
    const visibleArea = await navigation.boundingBox();
    expect(visibleArea!.y + visibleArea!.height).toBeLessThanOrEqual(640);
    const footer = page.locator('.sidebar-footer').filter({ visible: true });
    if (await footer.count()) {
      const footerArea = await footer.boundingBox();
      expect(visibleArea!.y + visibleArea!.height).toBeLessThanOrEqual(footerArea!.y);
    }
    await navigation.getByRole('menuitem', { name: '系统管理', exact: true }).click();
    const lastItem = page.getByRole('menuitem', { name: '账号安全', exact: true }).filter({ visible: true });
    await lastItem.scrollIntoViewIfNeeded();
    await expect(lastItem).toBeInViewport();
    await lastItem.click();
    await expect(page.getByRole('heading', { name: '账号安全', exact: true })).toBeVisible();
    if (width < 768) await expect(page.getByRole('dialog', { name: '工作空间导航', exact: true })).toBeHidden();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
  }
});
