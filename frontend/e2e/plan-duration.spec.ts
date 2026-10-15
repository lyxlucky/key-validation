import { test, expect } from '@playwright/test';
import { syntheticAdmin, fulfill } from './review-lifecycle-helpers';

const plan = {
  id: 'duration-plan', name: '时长测试卡', durationSeconds: 2592000,
  maxDevices: 1, maxConcurrentSessions: 1, activationValidityDays: null,
  features: [], isEnabled: true, referenceCount: 0, createdAt: '2026-10-01T00:00:00Z',
};

test.beforeEach(async ({ page }) => {
  await syntheticAdmin(page);
  await page.route('**/api/admin/plans**', route => fulfill(route,
    route.request().method() === 'GET'
      ? { items: [plan], total: 1, page: 1, pageSize: 20 } : plan));
  await page.goto('/plans');
});

for (const [unit, value, seconds] of [
  ['分钟', '90', 5400], ['小时', '2', 7200], ['天', '2', 172800],
  ['月', '2', 5184000], ['年', '2', 63072000],
] as const) {
  test(`saves authorization duration entered in ${unit} as seconds`, async ({ page }) => {
    await page.getByRole('button', { name: '创建卡种', exact: true }).click();
    const editor = page.getByRole('dialog', { name: '创建卡种', exact: true });
    await editor.getByLabel('卡种名称', { exact: true }).fill('新卡');
    await editor.getByLabel('时长单位', { exact: true }).click();
    await page.locator('.ant-select-dropdown').getByTitle(unit, { exact: true }).click();
    await editor.getByLabel('授权时长', { exact: true }).fill(value);
    const submitted = page.waitForRequest(request =>
      new URL(request.url()).pathname === '/api/admin/plans' && request.method() === 'POST');
    await editor.getByRole('button', { name: '保存卡种', exact: true }).click();
    expect((await submitted).postDataJSON().durationSeconds).toBe(seconds);
    await expect(editor).toBeHidden();
  });
}

test('editing an existing month card preserves its duration when saving other fields', async ({ page }) => {
  await page.getByRole('button', { name: '编辑', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '编辑卡种', exact: true });
  await expect(editor.getByLabel('授权时长', { exact: true })).toHaveValue('1');
  await expect(editor.getByText('月', { exact: true })).toBeVisible();
  await editor.getByLabel('设备上限', { exact: true }).fill('2');
  const submitted = page.waitForRequest(request => request.method() === 'PATCH');
  await editor.getByRole('button', { name: '保存卡种', exact: true }).click();
  expect((await submitted).postDataJSON().durationSeconds).toBe(2592000);
});

test('authorization duration enforces the ten year limit in years', async ({ page }) => {
  await page.getByRole('button', { name: '创建卡种', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '创建卡种', exact: true });
  await editor.getByLabel('时长单位', { exact: true }).click();
  await page.getByTitle('年', { exact: true }).click();
  await expect(editor.getByLabel('授权时长', { exact: true })).toHaveAttribute('aria-valuemax', '10');
});

for (const seconds of [1, 61, 5400, 86400, 31536000, 315360000]) {
  test(`editing preserves an existing duration of ${seconds} seconds`, async ({ page }) => {
    await page.route('**/api/admin/plans**', route => fulfill(route,
      route.request().method() === 'GET'
        ? { items: [{ ...plan, durationSeconds: seconds }], total: 1, page: 1, pageSize: 20 }
        : { ...plan, durationSeconds: seconds }));
    await page.reload();
    await page.getByRole('button', { name: '编辑', exact: true }).click();
    const editor = page.getByRole('dialog', { name: '编辑卡种', exact: true });
    const submitted = page.waitForRequest(request => request.method() === 'PATCH');
    await editor.getByRole('button', { name: '保存卡种', exact: true }).click();
    expect((await submitted).postDataJSON().durationSeconds).toBe(seconds);
    await expect(editor).toBeHidden();
  });
}

test('changing units revalidates a duration that exceeds ten years', async ({ page }) => {
  await page.getByRole('button', { name: '创建卡种', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '创建卡种', exact: true });
  await editor.getByLabel('卡种名称', { exact: true }).fill('超限卡');
  await editor.getByLabel('授权时长', { exact: true }).fill('11');
  await editor.getByLabel('时长单位', { exact: true }).click();
  await page.getByTitle('年', { exact: true }).click();
  await editor.getByRole('button', { name: '保存卡种', exact: true }).click();
  await expect(editor.getByText('授权时长须为 1 秒至 10 年', { exact: true })).toBeVisible();
  await expect(editor).toBeVisible();
});
