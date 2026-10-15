import { test, expect } from '@playwright/test';
import { catalog, login } from './roadmap-helpers';

test.use({
  trace: 'off', screenshot: 'off',
  // Chromium's headless default hides native scrollbars; exercise the same
  // visible mouse control users receive in a regular browser.
  launchOptions: {
    executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE,
    ignoreDefaultArgs: ['--hide-scrollbars'],
  },
});
test.afterEach(async ({ page }) => { await page.goto('about:blank'); });

test('workspace tabs retain filters, pagination, drafts and scroll, then release a closed page', async ({ page }) => {
  const { app } = await catalog(page, 22);
  await page.goto('/licenses');
  await page.getByLabel('筛选应用', { exact: true }).fill(app.name);
  await page.getByTitle(app.name, { exact: true }).click();
  await expect(page.getByText('共 22 张卡密', { exact: true })).toBeVisible();
  await page.locator('.ant-pagination-item-2').click();
  await page.getByLabel('备注关键字', { exact: true }).fill('尚未提交的筛选');
  const licensesPane = page.getByRole('tabpanel', { name: '卡密管理', exact: true });
  await expect(licensesPane).toBeVisible();
  await licensesPane.evaluate(element => { element.scrollTop = 240; });
  const scroll = await licensesPane.evaluate(element => element.scrollTop);
  expect(scroll).toBeGreaterThan(0);
  const nav = page.getByRole('navigation', { name: '主导航', exact: true });
  await nav.getByRole('menuitem', { name: '应用管理', exact: true }).click();
  await expect(page.getByRole('heading', { name: '应用管理', exact: true })).toBeVisible();
  const tabs = page.getByRole('tablist', { name: '工作区标签', exact: true });
  await tabs.getByRole('tab', { name: '卡密管理', exact: true }).click();
  await expect(page.getByLabel('备注关键字', { exact: true })).toHaveValue('尚未提交的筛选');
  await expect(licensesPane.locator('.ant-pagination-item-active')).toHaveText('2');
  await expect.poll(() => licensesPane.evaluate(element => element.scrollTop)).toBe(scroll);
  await tabs.getByRole('tab', { name: '应用管理', exact: true }).click();
  await page.goBack();
  await expect(tabs.getByRole('tab', { name: '卡密管理', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByLabel('备注关键字', { exact: true })).toHaveValue('尚未提交的筛选');
  await page.getByRole('button', { name: '关闭卡密管理标签', exact: true }).click();
  await expect(tabs.getByRole('tab', { name: '卡密管理', exact: true })).toHaveCount(0);
  await nav.getByRole('menuitem', { name: '卡密管理', exact: true }).click();
  await expect(page.getByLabel('备注关键字', { exact: true })).toHaveValue('');
  await expect(licensesPane.locator('.ant-pagination-item-active')).toHaveText('1');
  await page.getByRole('button', { name: '标签管理', exact: true }).click();
  await page.getByRole('menuitem', { name: '关闭其他标签', exact: true }).click();
  await expect(tabs.getByRole('tab')).toHaveCount(1);
  await page.getByRole('button', { name: '标签管理', exact: true }).click();
  await page.getByRole('menuitem', { name: '关闭全部标签', exact: true }).click();
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
  await expect(tabs.getByRole('tab')).toHaveCount(1);
});

test('overflow keeps the active close button visible and the tab strip scrollable by mouse', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 900 });
  await login(page);
  await page.getByRole('menuitem', { name: '授权管理', exact: true }).click();
  const strip = page.getByRole('tablist', { name: '工作区标签', exact: true });
  for (const name of ['应用管理', '卡密管理', '批次管理', '到期提醒', '客户端版本', '离线授权']) {
    await page.getByRole('menuitem', { name, exact: true }).click();
    // Populate every tab before measuring overflow; router updates can coalesce.
    await expect(strip.getByRole('tab', { name, exact: true })).toHaveAttribute('aria-selected', 'true');
  }
  await expect(strip.getByRole('tab')).toHaveCount(7);
  const geometry = await strip.evaluate(element => ({
    overflow: element.scrollWidth - element.clientWidth,
    scrollbarHeight: (element as HTMLElement).offsetHeight - element.clientHeight,
    scrollLeft: element.scrollLeft,
    width: element.clientWidth,
    tabs: [...element.querySelectorAll('[role="tab"]')].map(tab => ({ label: tab.textContent, selected: tab.getAttribute('aria-selected') })),
    path: location.pathname,
  }));
  expect(geometry.overflow, JSON.stringify(geometry)).toBeGreaterThan(0);
  await expect(page.getByRole('button', { name: '关闭离线授权标签', exact: true })).toBeInViewport({ ratio: 1 });
  expect(geometry.scrollbarHeight).toBeGreaterThan(0);
  const bounds = (await strip.boundingBox())!;
  const trackY = bounds.y + bounds.height - geometry.scrollbarHeight / 2;
  await page.mouse.move(bounds.x + bounds.width - 20, trackY);
  await page.mouse.down();
  await page.mouse.move(bounds.x + 20, trackY, { steps: 5 });
  await page.mouse.up();
  await expect.poll(() => strip.evaluate(element => element.scrollLeft)).toBeLessThan(geometry.scrollLeft);
  await strip.getByRole('tab', { name: '概览', exact: true }).click();
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
});

test('sidebar collapse persists and its grouped icon menus remain usable', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await login(page);
  await page.getByRole('button', { name: '收起侧边栏', exact: true }).click();
  await expect.poll(() => page.locator('.sidebar').evaluate(element => element.getBoundingClientRect().width)).toBe(76);
  await page.reload();
  await expect(page.getByRole('button', { name: '展开侧边栏', exact: true })).toBeVisible();
  await page.getByRole('menuitem', { name: '授权管理', exact: true }).click();
  await page.getByRole('menuitem', { name: '卡密管理', exact: true }).filter({ visible: true }).click();
  await expect(page.getByRole('heading', { name: '卡密管理', exact: true })).toBeVisible();
  await page.getByRole('button', { name: '展开侧边栏', exact: true }).click();
  await expect.poll(() => page.locator('.sidebar').evaluate(element => element.getBoundingClientRect().width)).toBe(232);
  await page.setViewportSize({ width: 768, height: 900 });
  await expect(page.getByRole('button', { name: '收起侧边栏', exact: true })).toBeVisible();
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(768);
});

test('switching tabs preserves an open policy form without leaking its dialog into another page', async ({ page }) => {
  const { app } = await catalog(page, 1);
  await page.goto('/applications');
  await page.getByRole('menuitem', { name: '运营与集成', exact: true }).click();
  await page.getByRole('menuitem', { name: '异常检测', exact: true }).click();
  await page.getByRole('button', { name: '风险策略', exact: true }).click();
  const policy = page.getByRole('dialog', { name: '风险策略', exact: true });
  await policy.getByLabel('策略应用', { exact: true }).fill(app.name);
  await page.getByTitle(app.name, { exact: true }).click();
  await policy.getByLabel('激活次数阈值', { exact: true }).fill('137');
  await page.goBack();
  await expect(page.getByRole('heading', { name: '应用管理', exact: true })).toBeVisible();
  await expect(policy).toBeHidden();
  await page.getByRole('tablist', { name: '工作区标签', exact: true }).getByRole('tab', { name: '异常检测', exact: true }).click();
  await expect(policy).toBeVisible();
  await expect(policy.getByLabel('激活次数阈值', { exact: true })).toHaveValue('137');
});

test('a license tab keeps its deep-link parameters while other pages are active', async ({ page }) => {
  const { issuance } = await catalog(page, 1);
  await page.goto(`/licenses?batchId=${issuance.batch.id}`);
  await expect(page.getByLabel('发行批次', { exact: true })).toHaveValue(issuance.batch.id);
  await page.getByRole('menuitem', { name: '应用管理', exact: true }).click();
  await page.getByRole('tablist', { name: '工作区标签', exact: true }).getByRole('tab', { name: '卡密管理', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`batchId=${issuance.batch.id}`));
  await expect(page.getByLabel('发行批次', { exact: true })).toHaveValue(issuance.batch.id);
});

test('rapid workspace clicks preserve the latest destination without duplicate history entries', async ({ page }) => {
  const { issuance } = await catalog(page, 1);
  await page.goto(`/licenses?batchId=${issuance.batch.id}`);
  await expect(page.getByLabel('发行批次', { exact: true })).toHaveValue(issuance.batch.id);
  const tabs = page.getByRole('tablist', { name: '工作区标签', exact: true });
  const applications = await page.getByRole('menuitem', { name: '应用管理', exact: true }).elementHandle();
  const licenses = await tabs.getByRole('tab', { name: '卡密管理', exact: true }).elementHandle();
  // Dispatch in one event turn so the second destination is requested before
  // React commits the first navigation. Repeated clicks must remain idempotent.
  const navigation = await page.evaluate(({ applications, licenses, batchId }) => {
    const start = history.length;
    (licenses as HTMLElement).click();
    (licenses as HTMLElement).click();
    const currentTabEntries = history.length - start;
    (applications as HTMLElement).click();
    (applications as HTMLElement).click();
    const applicationEntries = history.length - start;
    (licenses as HTMLElement).click();
    (licenses as HTMLElement).click();
    return { currentTabEntries, applicationEntries, totalEntries: history.length - start,
      pathname: location.pathname, retainsBatch: new URLSearchParams(location.search).get('batchId') === batchId };
  }, { applications, licenses, batchId: issuance.batch.id });
  expect(navigation).toEqual({ currentTabEntries: 0, applicationEntries: 1, totalEntries: 2, pathname: '/licenses', retainsBatch: true });
  await expect(page).toHaveURL(new RegExp(`batchId=${issuance.batch.id}`));
  await expect(page.getByLabel('发行批次', { exact: true })).toHaveValue(issuance.batch.id);
  await page.goBack();
  await expect(page.getByRole('heading', { name: '应用管理', exact: true })).toBeVisible();
  await page.goForward();
  await expect(tabs.getByRole('tab', { name: '卡密管理', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page).toHaveURL(new RegExp(`batchId=${issuance.batch.id}`));
  await expect(page.getByLabel('发行批次', { exact: true })).toHaveValue(issuance.batch.id);
  const close = await page.getByRole('button', { name: '关闭卡密管理标签', exact: true }).elementHandle();
  const applicationTab = await tabs.getByRole('tab', { name: '应用管理', exact: true }).elementHandle();
  const closeEntries = await page.evaluate(({ close, applicationTab }) => {
    const start = history.length;
    (close as HTMLElement).click();
    (applicationTab as HTMLElement).click();
    (applicationTab as HTMLElement).click();
    return history.length - start;
  }, { close, applicationTab });
  expect(closeEntries).toBe(1);
  await expect(page.getByRole('heading', { name: '应用管理', exact: true })).toBeVisible();
  await expect(tabs.getByRole('tab', { name: '卡密管理', exact: true })).toHaveCount(0);
});

test('confirmation dialogs stay with their workspace tab and are released when it closes', async ({ page }) => {
  const { app } = await catalog(page, 1);
  await page.goto('/licenses');
  await page.getByRole('menuitem', { name: '应用管理', exact: true }).click();
  await page.getByRole('row').filter({ hasText: app.name }).getByRole('button', { name: '禁用', exact: true }).click();
  const confirmation = page.getByRole('dialog', { name: `禁用应用「${app.name}」？`, exact: true });
  await expect(confirmation).toBeVisible();
  await page.goBack();
  await expect(page.getByRole('heading', { name: '卡密管理', exact: true })).toBeVisible();
  await expect(confirmation).toBeHidden();
  await page.getByRole('tablist', { name: '工作区标签', exact: true }).getByRole('tab', { name: '应用管理', exact: true }).click();
  await expect(confirmation).toBeVisible();
  await page.goBack();
  await expect(confirmation).toBeHidden();
  await page.getByRole('button', { name: '关闭应用管理标签', exact: true }).click();
  await page.getByRole('menuitem', { name: '应用管理', exact: true }).click();
  await expect(page.getByRole('heading', { name: '应用管理', exact: true })).toBeVisible();
  await expect(confirmation).toHaveCount(0);
  await expect(page.getByRole('row').filter({ hasText: app.name })).toContainText('运行中');
});

test('license operation drafts belong to their tab and are released when the tab closes', async ({ page }) => {
  const pageErrors: string[] = [];
  page.on('pageerror', error => pageErrors.push(error.message));
  const { issuance } = await catalog(page, 1);
  await page.goto('/applications');
  await page.getByRole('menuitem', { name: '卡密管理', exact: true }).click();
  await page.getByRole('row').filter({ hasText: issuance.licenses[0].keyHint }).getByRole('button', { name: '详情', exact: true }).click();
  await page.getByRole('button', { name: '续期卡密', exact: true }).click();
  const operation = page.getByRole('dialog', { name: '续期卡密', exact: true });
  await operation.getByLabel('延长时长（小时）', { exact: true }).fill('13');
  await operation.getByLabel('操作原因', { exact: true }).fill('切换页面保留草稿');
  await page.goBack();
  await expect(page.getByRole('heading', { name: '应用管理', exact: true })).toBeVisible();
  await expect(operation).toBeHidden();
  await page.getByRole('tablist', { name: '工作区标签', exact: true }).getByRole('tab', { name: '卡密管理', exact: true }).click();
  await expect(operation).toBeVisible();
  await expect(operation.getByLabel('延长时长（小时）', { exact: true })).toHaveValue('13');
  await expect(operation.getByLabel('操作原因', { exact: true })).toHaveValue('切换页面保留草稿');
  await page.goBack();
  await expect(operation).toBeHidden();
  await page.getByRole('button', { name: '关闭卡密管理标签', exact: true }).click();
  await page.getByRole('menuitem', { name: '卡密管理', exact: true }).click();
  await expect(operation).toHaveCount(0);
  await page.getByRole('row').filter({ hasText: issuance.licenses[0].keyHint }).getByRole('button', { name: '详情', exact: true }).click();
  await page.getByRole('button', { name: '续期卡密', exact: true }).click();
  await expect(operation.getByLabel('延长时长（小时）', { exact: true })).toHaveValue('24');
  await expect(operation.getByLabel('操作原因', { exact: true })).toHaveValue('');
  expect(pageErrors).toEqual([]);
});

test('a submitted operation can be recovered from another tab after its original tab closes', async ({ page }) => {
  const { issuance } = await catalog(page, 1);
  const licenseId = issuance.licenses[0].id;
  const before = await (await page.request.get(`/api/admin/licenses/${licenseId}`)).json();
  await page.goto('/applications');
  await page.getByRole('menuitem', { name: '卡密管理', exact: true }).click();
  await page.getByRole('row').filter({ hasText: issuance.licenses[0].keyHint }).getByRole('button', { name: '详情', exact: true }).click();
  await page.getByRole('button', { name: '续期卡密', exact: true }).click();
  await page.getByLabel('延长时长（小时）', { exact: true }).fill('1');
  await page.getByLabel('操作原因', { exact: true }).fill('跨标签恢复原请求');
  let submissions = 0;
  await page.route('**/api/admin/license-operations', async route => {
    if (route.request().method() !== 'POST') return route.continue();
    submissions++;
    expect((await route.fetch()).status()).toBe(200);
    await route.abort('failed');
  });
  await page.getByRole('button', { name: '确认执行', exact: true }).click();
  await expect(page.getByText('连接中断，操作结果尚未确认。请检查操作结果，或使用原请求重试。')).toBeVisible();
  await page.goBack();
  await expect(page.getByRole('dialog', { name: '续期卡密', exact: true })).toBeHidden();
  await page.getByRole('button', { name: '关闭卡密管理标签', exact: true }).click();
  await page.getByRole('button', { name: '处理待确认操作', exact: true }).click();
  await page.getByRole('button', { name: '检查操作结果', exact: true }).click();
  await expect(page.getByText('成功 1 张，失败 0 张', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: '完成', exact: true }).click();
  await expect(page.getByRole('button', { name: '处理待确认操作', exact: true })).toHaveCount(0);
  const after = await (await page.request.get(`/api/admin/licenses/${licenseId}`)).json();
  expect(after.license.durationSeconds - before.license.durationSeconds).toBe(3600);
  expect(submissions).toBe(1);
});
