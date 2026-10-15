import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { expectTableFilterSpacing } from './layout-helpers';

// Synthetic secrets only: these scenarios do not create production records.
test.use({ trace: 'off', screenshot: 'off' });
const applications = Array.from({ length: 65 }, (_, i) => ({
  id: `app-${i}`, name: i === 64 ? '目标桌面工具' : `测试应用 ${i}`,
  code: `desktop-${i}`, disabledAt: null, createdAt: '2026-10-09T00:00:00Z',
}));
const plan = { id: 'plan-1', name: '月卡', durationSeconds: 2592000, maxDevices: 2,
  maxConcurrentSessions: 1, activationValidityDays: null, features: [], isEnabled: true };

async function setup(page: Page) {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.addInitScript(() => {
    Object.assign(window, { copiedText: '' });
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: {
      writeText: async (value: string) => { Object.assign(window, { copiedText: value }); },
    } });
  });
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    const pathname = url.pathname.replace('/api', '');
    let data: unknown = { items: [], total: 0, page: 1, pageSize: 20 };
    if (pathname === '/admin/auth/me') data = { id: 'qa-user', userName: 'QA', roles: ['Admin'], mustChangePassword: false, twoFactorEnabled: false };
    else if (pathname === '/admin/auth/access') data = { permissions: ['platform.manage', 'licenses.read', 'licenses.issue', 'licenses.manage', 'reports.read', 'customers.manage', 'orders.manage', 'integrations.manage', 'audit.read', 'risk.review'], allApplications: true, applicationIds: [], channelId: null };
    else if (pathname === '/admin/auth/csrf') data = { token: 'qa-token' };
    else if (pathname === '/admin/applications') {
      const current = Number(url.searchParams.get('page') ?? 1);
      const size = Number(url.searchParams.get('pageSize') ?? 20);
      data = { items: applications.slice((current - 1) * size, current * size), total: applications.length, page: current, pageSize: size };
    } else if (/\/applications\/[^/]+\/plans$/.test(pathname)) data = { items: [{ ...plan, applicationId: pathname.split('/')[3] }], total: 1, page: 1, pageSize: 100 };
    else if (pathname.endsWith('/trial-policy')) data = { isEnabled: false, durationSeconds: 86400, features: [] };
    else if (pathname === '/admin/dashboard') data = { totalLicenses: 0, activatedLicenses: 0, validLicenses: 0, onlineSessions: 0 };
    else if (pathname === '/admin/catalog') data = { applications, plans: [] };
    else if (pathname === '/admin/license-batches' && route.request().method() === 'POST') {
      const body = route.request().postDataJSON();
      data = { batch: { id: 'qa-batch', quantity: body.quantity }, licenses: Array.from({ length: body.quantity }, (_, i) => ({ id: `qa-license-${i}`, key: `QA-SYNTHETIC-KEY-${i}` })) };
    }
    await route.fulfill({ json: data });
  });
  return errors;
}

test('application creation needs only a name and identifiers can be copied', async ({ page }) => {
  const errors = await setup(page);
  await page.goto('/applications');
  let created: { name: string; code: string } | undefined;
  await page.route('**/api/admin/applications', async route => {
    if (route.request().method() !== 'POST') return route.fallback();
    created = route.request().postDataJSON();
    await route.fulfill({ status: 201, json: { ...applications[0], ...created } });
  });
  await page.getByRole('button', { name: /创建应用$/ }).click();
  const dialog = page.getByRole('dialog', { name: '创建应用', exact: true });
  await dialog.getByLabel('应用名称', { exact: true }).fill('自动标识应用');
  await dialog.getByRole('button', { name: '创建', exact: true }).click();
  await expect(dialog).toBeHidden();
  expect(created?.name).toBe('自动标识应用');
  expect(created?.code).toMatch(/^app_[a-f0-9]{32}$/);
  const settings = page.getByRole('dialog', { name: /· 应用设置$/ });
  await expect(settings).toBeVisible();
  await expectTableFilterSpacing(page);
  await settings.getByRole('button', { name: '关闭', exact: true }).click();
  await page.getByRole('row').filter({ hasText: 'desktop-0' }).getByRole('button', { name: '复制应用标识', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as unknown as { copiedText: string }).copiedText)).toBe('desktop-0');
  expect(errors).toEqual([]);
});

test('issuance searches beyond the first application page and replaces its dialog continuously', async ({ page }) => {
  const errors = await setup(page);
  await page.goto('/licenses');
  await page.getByRole('button', { name: /生成卡密$/ }).click();
  const config = page.getByRole('dialog', { name: '生成卡密', exact: true });
  await expect(config.getByLabel('卡种', { exact: true })).toBeDisabled();
  await config.getByLabel('所属应用', { exact: true }).fill('desktop-64');
  await page.getByTitle('目标桌面工具', { exact: true }).click();
  await config.getByLabel('卡种', { exact: true }).fill('月');
  await page.getByTitle('月卡', { exact: true }).click();
  const original = await config.getAttribute('data-testid');
  await config.getByLabel('生成数量', { exact: true }).fill('2');
  await config.getByRole('button', { name: '确认生成', exact: true }).click();
  const result = page.getByRole('dialog', { name: '卡密已生成', exact: true });
  await expect(result).toBeVisible();
  await expect(result.getByRole('heading', { name: '已生成 2 张卡密', exact: true })).toBeFocused();
  expect(await result.getAttribute('data-testid')).toBe(original);
  await expect(page.locator('.ant-modal:visible')).toHaveCount(1);
  await result.getByRole('button', { name: '复制全部', exact: true }).click();
  await expect.poll(() => page.evaluate(() => (window as unknown as { copiedText: string }).copiedText)).toBe('QA-SYNTHETIC-KEY-0\nQA-SYNTHETIC-KEY-1');
  const downloaded = page.waitForEvent('download');
  await result.getByRole('button', { name: '下载 CSV', exact: true }).click();
  expect((await downloaded).suggestedFilename()).toBe('keygate-qa-batch.csv');
  await result.getByRole('button', { name: '已保存，关闭', exact: true }).click();
  await expect(result).toBeHidden();
  await expect(page.getByTestId('issued-key')).toHaveCount(0);
  await page.getByRole('button', { name: /生成卡密$/ }).click();
  await expect(config.getByLabel('所属应用', { exact: true })).toHaveValue('');
  expect(errors).toEqual([]);
});

for (const [path, label] of [['/batches', '应用'], ['/expiring', '应用']] as const) {
  test(`${path} application filter supports search`, async ({ page }) => {
    await setup(page);
    await page.goto(path);
    await page.getByLabel(label, { exact: true }).fill('目标桌面');
    await expect(page.getByTitle('目标桌面工具', { exact: true })).toBeVisible();
    await expect(page.getByTitle('测试应用 0', { exact: true })).toHaveCount(0);
  });
}

test('issuance failure preserves the original request for a retry', async ({ page }) => {
  await setup(page);
  await page.goto('/licenses');
  await page.getByRole('button', { name: /生成卡密$/ }).click();
  const dialog = page.getByRole('dialog', { name: '生成卡密', exact: true });
  await dialog.getByLabel('所属应用', { exact: true }).fill('目标');
  await page.getByTitle('目标桌面工具', { exact: true }).click();
  await dialog.getByLabel('卡种', { exact: true }).click();
  await page.getByTitle('月卡', { exact: true }).click();
  const requests: unknown[] = [];
  await page.route('**/api/admin/license-batches', async route => {
    requests.push(route.request().postDataJSON());
    await route.fulfill({ status: 503, json: { code: 'INTERNAL_ERROR' } });
  });
  await dialog.getByRole('button', { name: '确认生成', exact: true }).click();
  await expect(dialog.getByText('存在待确认的发卡请求', { exact: true })).toBeVisible();
  await dialog.getByRole('button', { name: '使用原请求重试', exact: true }).click();
  await expect.poll(() => requests.length).toBe(2);
  expect(requests[1]).toEqual(requests[0]);
  await expect(dialog.getByLabel('所属应用', { exact: true })).toBeDisabled();
});

test('application search finds names and codes on later pages', async ({ page }) => {
  await setup(page);
  await page.goto('/applications');
  await page.getByLabel('搜索应用', { exact: true }).fill('desktop-64');
  await expect(page.getByRole('row').filter({ hasText: '目标桌面工具' })).toBeVisible();
  await page.getByLabel('搜索应用', { exact: true }).fill('不存在的应用');
  await expect(page.getByText('没有匹配的应用，请调整搜索条件。', { exact: true })).toBeVisible();
  await page.getByLabel('搜索应用', { exact: true }).fill('');
  await expect(page.getByRole('row').filter({ hasText: 'desktop-0' })).toBeVisible();
});

for (const width of [375, 768, 1440]) {
  test(`plan filters and application settings leave space before tables at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await setup(page);
    await page.goto('/plans');
    await expect(page.getByLabel('搜索卡种', { exact: true })).toBeVisible();
    await expectTableFilterSpacing(page);
    await page.screenshot({ path: `../.local/usability-preview/plan-filter-spacing-${width}.png`, animations: 'disabled' });
    await page.goto('/applications');
    await page.getByRole('row').filter({ hasText: 'desktop-0' }).getByRole('button', { name: '应用设置', exact: true }).click();
    await expect(page.getByLabel('选择全局卡种', { exact: true })).toBeVisible();
    await expectTableFilterSpacing(page);
    await page.screenshot({ path: `../.local/usability-preview/application-settings-spacing-${width}.png`, animations: 'disabled' });
  });
}

test('Webhook creation closes its editor before presenting the one-time secret', async ({ page }) => {
  await setup(page);
  await page.route('**/api/admin/webhook-subscriptions', async route => {
    if (route.request().method() !== 'POST') return route.fallback();
    await route.fulfill({ json: { secret: 'QA-SYNTHETIC-WEBHOOK-SECRET' } });
  });
  await page.goto('/integrations');
  await page.getByRole('tab', { name: 'Webhook', exact: true }).click();
  await page.getByRole('button', { name: '创建订阅', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '创建 Webhook 订阅', exact: true });
  await editor.getByLabel('所属应用', { exact: true }).fill('目标');
  await page.getByTitle('目标桌面工具', { exact: true }).click();
  await editor.getByLabel('订阅名称', { exact: true }).fill('测试订阅');
  await editor.getByLabel('接收地址', { exact: true }).fill('https://example.com/events');
  await editor.getByRole('button', { name: '保存订阅', exact: true }).click();
  const result = page.getByRole('dialog', { name: '保存 Webhook 签名密钥', exact: true });
  await expect(result).toBeVisible();
  await expect(editor).toBeHidden();
  await expect(page.locator('.ant-modal:visible')).toHaveCount(1);
  await result.getByRole('button', { name: '已保存，关闭', exact: true }).click();
  await expect(page.getByTestId('one-time-secret')).toHaveCount(0);
});

test('issuance explains an empty plan list and disables cancellation during submission', async ({ page }) => {
  await setup(page);
  await page.route('**/api/admin/applications/app-0/plans?*', route => route.fulfill({ json: { items: [], total: 0, page: 1, pageSize: 100 } }));
  await page.goto('/licenses');
  await page.getByRole('button', { name: /生成卡密$/ }).click();
  const dialog = page.getByRole('dialog', { name: '生成卡密', exact: true });
  await dialog.getByLabel('所属应用', { exact: true }).fill('desktop-0');
  await page.getByTitle('测试应用 0', { exact: true }).click();
  await expect(dialog.getByText(/新卡种在卡种管理中创建/)).toBeVisible();
  await dialog.getByLabel('所属应用', { exact: true }).fill('desktop-64');
  await page.getByTitle('目标桌面工具', { exact: true }).click();
  await dialog.getByLabel('卡种', { exact: true }).click();
  await page.getByTitle('月卡', { exact: true }).click();
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/admin/license-batches', async route => { await gate; await route.fallback(); });
  try {
    await dialog.getByRole('button', { name: '确认生成', exact: true }).click();
    await expect(dialog.getByRole('button', { name: '取消', exact: true })).toBeDisabled();
    await expect(dialog.getByRole('button', { name: '关闭', exact: true })).toBeDisabled();
  } finally { release(); }
  await expect(page.getByRole('dialog', { name: '卡密已生成', exact: true })).toBeVisible();
});

for (const reducedMotion of ['no-preference', 'reduce'] as const) {
  test(`mobile issuance result stays usable with motion ${reducedMotion}`, async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion });
    await setup(page);
    await page.goto('/licenses');
    await page.getByRole('button', { name: /生成卡密$/ }).click();
    const config = page.getByRole('dialog', { name: '生成卡密', exact: true });
    await config.getByLabel('所属应用', { exact: true }).fill('目标');
    await page.getByTitle('目标桌面工具', { exact: true }).click();
    await config.getByLabel('卡种', { exact: true }).click();
    await page.getByTitle('月卡', { exact: true }).click();
    await config.getByRole('button', { name: '确认生成', exact: true }).click();
    const result = page.getByRole('dialog', { name: '卡密已生成', exact: true });
    await expect(result.getByRole('button', { name: '下载 CSV', exact: true })).toBeInViewport();
    const box = await result.boundingBox();
    expect(box!.x).toBeGreaterThanOrEqual(0);
    expect(box!.x + box!.width).toBeLessThanOrEqual(375);
    const duration = await result.locator('.issuance-stage').evaluate(element => getComputedStyle(element).animationDuration);
    expect(duration).toBe(reducedMotion === 'reduce' ? '1e-05s' : '0.18s');
    await result.getByRole('button', { name: '已保存，关闭', exact: true }).click();
    await expect(page.getByRole('dialog', { name: '尚未复制或下载卡密', exact: true })).toBeVisible();
    await page.getByRole('button', { name: '返回保存', exact: true }).click();
    await expect(result).toBeVisible();
    await page.screenshot({ path: `../.local/usability-preview/issuance-result-375-${reducedMotion}.png` });
    await result.getByRole('button', { name: '复制全部', exact: true }).click();
    await result.getByRole('button', { name: '已保存，关闭', exact: true }).click();
    await expect(page.getByTestId('issued-key')).toHaveCount(0);
  });
}
