import { test, expect } from '@playwright/test';
import { login } from './roadmap-helpers';
import { expectTableFilterSpacing } from './layout-helpers';

test.use({ trace: 'off', screenshot: 'off' });
const routes = [
  ['overview', '概览'], ['applications', '应用管理'], ['plans', '卡种管理'], ['licenses', '卡密管理'],
  ['batches', '批次管理'], ['expiring', '到期提醒'], ['client-policies', '客户端版本'],
  ['offline-licenses', '离线授权'], ['customers', '客户管理'], ['orders', '订单管理'],
  ['portal-requests', '客户申请'], ['channels', '渠道管理'], ['reports', '运营统计'],
  ['risk', '异常检测'], ['notifications', '通知中心'], ['integrations', '开放集成'],
  ['operations', '运维中心'], ['accounts', '账号管理'], ['roles', '角色管理'],
  ['audit', '操作日志'], ['login-logs', '登录日志'], ['security', '账号安全'],
];

for (const width of [375, 768, 1440]) {
  test(`all administration pages work at ${width}px against the local API`, async ({ page }) => {
    test.skip(!process.env.E2E_ADMIN_USER, 'Requires a local QA account');
    test.setTimeout(180000);
    await page.setViewportSize({ width, height: 900 });
    await login(page);
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    for (const [route, title] of routes) {
      await page.goto('/' + route);
      await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
      await expect.poll(() => page.locator('.ant-spin-spinning:visible').count()).toBe(0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), route).toBe(true);
      await expectTableFilterSpacing(page);
      if (['applications', 'licenses', 'plans', 'integrations'].includes(route)) {
        await page.screenshot({ path: `../.local/usability-preview/${route}-${width}.png`, fullPage: true, animations: 'disabled' });
      }
    }
    expect(errors).toEqual([]);
  });
}
