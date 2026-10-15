import { test, expect } from '@playwright/test';
import type { Locator, Page } from '@playwright/test';
import { login } from './roadmap-helpers';

test.use({ trace: 'off', screenshot: 'off' });

async function openNavigation(page: Page, width: number) {
  if (width < 768) {
    await expect(page.getByRole('dialog', { name: '工作空间导航', exact: true })).toBeHidden();
    await page.getByRole('button', { name: '打开导航', exact: true }).click();
  }
  return page.getByRole('navigation', { name: '主导航', exact: true }).filter({ visible: true });
}

async function closeNavigation(page: Page, width: number) {
  if (width < 768) {
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog', { name: '工作空间导航', exact: true })).toBeHidden();
  }
}

async function expectOnlyGroup(nav: Locator, name: string) {
  await expect(nav.getByRole('menuitem', { name, exact: true })).toHaveAttribute('aria-expanded', 'true');
  await expect(nav.locator('[role="menuitem"][aria-expanded="true"]')).toHaveCount(1);
}

for (const [device, width] of [['desktop', 1440], ['phone', 375]] as const) {
  test(`navigation opens one group at a time and allows closing it on ${device}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 812 });
    await login(page);
    await page.goto('/licenses');
    const nav = await openNavigation(page, width);
    const licenses = nav.getByRole('menuitem', { name: '授权管理', exact: true });
    await expectOnlyGroup(nav, '授权管理');
    await expect(nav.getByRole('menuitem', { name: '卡密管理', exact: true })).toBeVisible();
    await expect(nav.getByRole('menuitem', { name: '账号管理', exact: true })).toBeHidden();

    for (const name of ['客户与渠道', '运营与集成', '系统管理', '授权管理']) {
      await nav.getByRole('menuitem', { name, exact: true }).click();
      await expectOnlyGroup(nav, name);
    }
    await licenses.click();
    await expect(nav.locator('[role="menuitem"][aria-expanded="true"]')).toHaveCount(0);
    await expect(nav.getByRole('menuitem', { name: '卡密管理', exact: true })).toBeHidden();
    await licenses.click();
    await expectOnlyGroup(nav, '授权管理');
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await closeNavigation(page, width);
  });

  test(`tabs and browser history reveal only the active navigation group on ${device}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 812 });
    await login(page);
    await page.goto('/applications');
    const nav = await openNavigation(page, width);
    const tabs = page.getByRole('tablist', { name: '工作区标签', exact: true });
    await nav.getByRole('menuitem', { name: '卡密管理', exact: true }).click();
    await expect(page.getByRole('heading', { name: '卡密管理', exact: true })).toBeVisible();
    await openNavigation(page, width);
    await nav.getByRole('menuitem', { name: '授权管理', exact: true }).click();
    await nav.getByRole('menuitem', { name: '运营与集成', exact: true }).click();
    await closeNavigation(page, width);

    // Both tabs belong to the same group, which may have been closed manually.
    await tabs.getByRole('tab', { name: '应用管理', exact: true }).click();
    await expect(page.getByRole('heading', { name: '应用管理', exact: true })).toBeVisible();
    await openNavigation(page, width);
    await expectOnlyGroup(nav, '授权管理');
    await expect(nav.locator('.ant-menu-item-selected')).toHaveText('应用管理');
    await nav.getByRole('menuitem', { name: '授权管理', exact: true }).click();
    await closeNavigation(page, width);

    await page.goBack();
    await expect(page.getByRole('heading', { name: '卡密管理', exact: true })).toBeVisible();
    await openNavigation(page, width);
    await expectOnlyGroup(nav, '授权管理');
    await expect(nav.locator('.ant-menu-item-selected')).toHaveText('卡密管理');
    await nav.getByRole('menuitem', { name: '运营与集成', exact: true }).click();
    await closeNavigation(page, width);

    await page.goForward();
    await expect(page.getByRole('heading', { name: '应用管理', exact: true })).toBeVisible();
    await openNavigation(page, width);
    await expectOnlyGroup(nav, '授权管理');
    await nav.getByRole('menuitem', { name: '系统管理', exact: true }).click();
    await nav.getByRole('menuitem', { name: '账号安全', exact: true }).click();
    await expect(page.getByRole('heading', { name: '账号安全', exact: true })).toBeVisible();
    await openNavigation(page, width);
    await expectOnlyGroup(nav, '系统管理');
    await closeNavigation(page, width);

    await tabs.getByRole('tab', { name: '应用管理', exact: true }).click();
    await openNavigation(page, width);
    await expectOnlyGroup(nav, '授权管理');
    await expect(nav.locator('.ant-menu-item-selected')).toHaveText('应用管理');
    await closeNavigation(page, width);
  });
}

test('collapsed sidebar opens one popup at a time and restores the active group when expanded', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 812 });
  await login(page);
  await page.goto('/licenses');
  await page.getByRole('button', { name: '收起侧边栏', exact: true }).click();
  const nav = await openNavigation(page, 1440);
  const licenses = nav.getByRole('menuitem', { name: '授权管理', exact: true });
  const system = nav.getByRole('menuitem', { name: '系统管理', exact: true });
  await expect(nav.locator('[role="menuitem"][aria-expanded="true"]')).toHaveCount(0);
  await expect(nav.getByRole('menuitem', { name: '卡密管理', exact: true })).toBeHidden();
  await licenses.click();
  await expectOnlyGroup(nav, '授权管理');
  await expect(page.getByRole('menuitem', { name: '卡密管理', exact: true }).filter({ visible: true })).toHaveCount(1);
  await system.click();
  await expectOnlyGroup(nav, '系统管理');
  await expect(page.getByRole('menuitem', { name: '卡密管理', exact: true }).filter({ visible: true })).toBeHidden();
  await system.click();
  await expect(nav.locator('[role="menuitem"][aria-expanded="true"]')).toHaveCount(0);
  await expect(page.getByRole('menuitem', { name: '账号安全', exact: true }).filter({ visible: true })).toBeHidden();

  await system.click();
  await page.getByRole('menuitem', { name: '账号安全', exact: true }).filter({ visible: true }).click();
  await expect(page.getByRole('heading', { name: '账号安全', exact: true })).toBeVisible();
  await expect(nav.locator('[role="menuitem"][aria-expanded="true"]')).toHaveCount(0);
  await page.getByRole('button', { name: '展开侧边栏', exact: true }).click();
  await expectOnlyGroup(nav, '系统管理');
  await expect(nav.locator('.ant-menu-item-selected')).toHaveText('账号安全');
});
