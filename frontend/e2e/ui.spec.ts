import { test, expect } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

test.use({ trace: 'off', screenshot: 'off' });

const screenshotDirectory = path.resolve('..', '.local', 'ui-preview');

test('mobile navigation changes page, dismisses and restores keyboard focus', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto('/login');
  await page.getByLabel('用户名', { exact: true }).fill(process.env.E2E_ADMIN_USER!);
  await page.getByLabel('密码', { exact: true }).fill(process.env.E2E_ADMIN_PASSWORD!);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
  const trigger = page.getByRole('button', { name: '打开导航' });
  await trigger.click();
  const drawer = page.getByRole('dialog', { name: '工作空间导航' });
  await expect(drawer).toBeVisible();
  await drawer.getByRole('menuitem', { name: '授权管理', exact: true }).click();
  await drawer.getByRole('menuitem', { name: '应用管理' }).click();
  await expect(page.getByRole('heading', { name: '应用管理', exact: true })).toBeVisible();
  await expect(drawer).toBeHidden();
  await expect(page.getByRole('main')).toBeFocused();
  await trigger.click();
  await expect(drawer).toBeVisible();
  await drawer.getByRole('link', { name: 'KeyGate 首页' }).click();
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
  await expect(drawer).toBeHidden();
  await trigger.click();
  await page.keyboard.press('Escape');
  await expect(drawer).toBeHidden();
  await expect(trigger).toBeFocused();
});

test('all pages fit phone, tablet and desktop viewports', async ({ page }) => {
  test.setTimeout(120000);
  await mkdir(screenshotDirectory, { recursive: true });
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/login');
  for (const width of [375, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 960 });
    await expect(page.getByRole('heading', { name: '登录管理后台' })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await page.screenshot({ path: path.join(screenshotDirectory, `login-${width}.png`), fullPage: true, animations: 'disabled' });
  }
  await page.getByLabel('用户名', { exact: true }).fill(process.env.E2E_ADMIN_USER!);
  await page.getByLabel('密码', { exact: true }).fill(process.env.E2E_ADMIN_PASSWORD!);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
  for (const width of [375, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 960 });
    for (const [route, title] of [['overview', '概览'], ['applications', '应用管理'], ['plans', '卡种管理'], ['licenses', '卡密管理'], ['audit', '操作日志'], ['login-logs', '登录日志'], ['roles', '角色管理'], ['accounts', '账号管理'], ['batches', '批次管理'], ['expiring', '到期提醒'], ['security', '账号安全']]) {
      await page.goto(`/${route}`);
      await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
      await expect(page.locator('.ant-spin-spinning')).toHaveCount(0);
      await page.evaluate(() => document.fonts.ready);
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      if (width < 768) await expect(page.getByRole('button', { name: '打开导航' })).toBeVisible();
      await page.screenshot({ path: path.join(screenshotDirectory, `${route}-${width}.png`), fullPage: true, animations: 'disabled' });
    }
  }
  expect(errors).toEqual([]);
});

test('mobile management overlays and date calendar remain usable', async ({ page }) => {
  await mkdir(screenshotDirectory, { recursive: true });
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto('/login');
  await page.getByLabel('用户名', { exact: true }).fill(process.env.E2E_ADMIN_USER!);
  await page.getByLabel('密码', { exact: true }).fill(process.env.E2E_ADMIN_PASSWORD!);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();

  await page.goto('/applications');
  await page.getByRole('button', { name: /创建应用$/ }).click();
  await expect(page.getByLabel('应用名称', { exact: true })).toBeVisible();
  await page.screenshot({ path: path.join(screenshotDirectory, 'create-application-375.png'), animations: 'disabled' });
  await page.getByRole('button', { name: '取消', exact: true }).click();
await page.getByRole('button', { name: '应用设置', exact: true }).first().click();
const settings = page.getByRole('dialog', { name: /· 应用设置$/ });
await expect(settings.getByRole('heading', { name: '引用卡种', exact: true })).toBeVisible();
await expect(settings.getByRole('heading', { name: '试用授权', exact: true })).toBeVisible();
await settings.getByRole('button', { name: '关闭', exact: true }).click();
await page.goto('/plans');
await page.getByRole('button', { name: '创建卡种', exact: true }).click();
await expect(page.getByLabel('授权时长', { exact: true })).toBeVisible();
await expect(page.getByRole('button', { name: '保存卡种', exact: true })).toBeInViewport();
await page.getByRole('button', { name: '取消', exact: true }).click();

  await page.goto('/licenses');
  await page.getByRole('button', { name: /生成卡密$/ }).click();
  await expect(page.getByLabel('所属应用')).toBeVisible();
  await expect(page.getByRole('button', { name: '确认生成', exact: true })).toBeInViewport();
  await page.screenshot({ path: path.join(screenshotDirectory, 'issue-375.png'), animations: 'disabled' });
  await page.getByRole('button', { name: '取消', exact: true }).click();
  await page.getByRole('button', { name: '详情', exact: true }).first().click();
  const detail = page.getByRole('dialog', { name: '卡密详情', exact: true });
  await expect(detail.getByText('授权信息', { exact: true })).toBeVisible();
  expect(await detail.locator('.ant-drawer-body').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  await page.screenshot({ path: path.join(screenshotDirectory, 'license-detail-375.png'), animations: 'disabled' });
  await detail.getByRole('button', { name: '禁用卡密', exact: true }).click();
  await expect(page.getByRole('button', { name: '确认禁用', exact: true })).toBeInViewport();
  await page.screenshot({ path: path.join(screenshotDirectory, 'confirm-disable-375.png'), animations: 'disabled' });
  await page.getByRole('button', { name: '取消', exact: true }).click();
  await detail.getByRole('button', { name: '关闭', exact: true }).click();

  await page.goto('/audit');
  await page.getByLabel('日期范围', { exact: true }).click();
  const calendar = page.locator('.ant-picker-dropdown:visible');
  await expect(calendar).toBeVisible();
  await expect.poll(async () => {
    const box = await calendar.boundingBox();
    return box ? box.x >= 0 && box.x + box.width <= 375 && box.y >= 0 && box.y + box.height <= 812 : false;
  }).toBe(true);
  await page.screenshot({ path: path.join(screenshotDirectory, 'audit-calendar-375.png'), animations: 'disabled' });
  const visibleDates = calendar.locator('.ant-picker-cell-in-view[title]');
  const start = await visibleDates.nth(4).getAttribute('title');
  const end = await visibleDates.nth(7).getAttribute('title');
  await calendar.locator(`[title="${start}"]`).first().click();
  const filteredRequest = page.waitForResponse(response => response.url().includes('/api/admin/audit-logs?') && response.url().includes('from=') && response.url().includes('to='));
  await calendar.locator(`[title="${end}"]`).first().click();
  expect((await filteredRequest).status()).toBe(200);
  await expect(page.getByLabel('日期范围', { exact: true })).toHaveValue(start!);
  await expect(page.getByLabel('结束日期', { exact: true })).toHaveValue(end!);
  await page.getByRole('button', { name: '重置' }).click();
  await expect(page.getByLabel('日期范围', { exact: true })).toHaveValue('');
  await expect(page.getByLabel('结束日期', { exact: true })).toHaveValue('');
});

test('bottom page shortcuts open the destination at its heading', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto('/login');
  await page.getByLabel('用户名', { exact: true }).fill(process.env.E2E_ADMIN_USER!);
  await page.getByLabel('密码', { exact: true }).fill(process.env.E2E_ADMIN_PASSWORD!);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
  await page.getByRole('button', { name: '查看操作日志' }).click();
  await expect(page.getByRole('heading', { name: '操作日志', exact: true })).toBeInViewport();
  await expect(page.getByRole('main')).toBeFocused();
});
