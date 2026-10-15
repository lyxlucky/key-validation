import { test, expect } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { catalog } from './roadmap-helpers';

test.use({ trace: 'off', screenshot: 'off' });
test.afterEach(async ({ page }) => { if (!page.isClosed()) await page.goto('about:blank'); });

test('administrators can select a channel consistently in licensing, batches, reports and expiry', async ({ page }) => {
  const { app, plan, headers } = await catalog(page, 1);
  const suffix = randomUUID().slice(0, 8), name = `业务渠道-${suffix}`;
  const accountResponse = await page.request.post('/api/admin/accounts', { headers, data: { userName: `filters-${suffix}`, password: `${randomUUID()}aA1!`, role: 'Operator' } });
  expect(accountResponse.status()).toBe(201);
  const account = await accountResponse.json();
  const channelResponse = await page.request.post('/api/admin/channels', { headers, data: { name, accountId: account.id, applicationIds: [app.id], initialQuota: 1 } });
  expect(channelResponse.status()).toBe(201);
  const channel = await channelResponse.json();
  expect((await page.request.put(`/api/admin/channels/${channel.id}/prices`, { headers, data: { applicationId: app.id, planId: plan.id, unitPriceCents: 1000 } })).ok()).toBe(true);
  const customerResponse = await page.request.post('/api/admin/customers', { headers, data: { applicationId: app.id, channelId: channel.id, name: `渠道客户-${suffix}` } });
  expect(customerResponse.status()).toBe(201);
  const customer = await customerResponse.json();
  const orderResponse = await page.request.post('/api/admin/orders', { headers, data: { requestId: randomUUID(), applicationId: app.id, customerId: customer.id, planId: plan.id, type: 'issue', quantity: 1, unitPriceCents: 0 } });
  expect(orderResponse.status()).toBe(201);
  const order = await orderResponse.json();
  expect((await page.request.post(`/api/admin/orders/${order.id}/fulfill`, { headers, data: { requestId: randomUUID() } })).ok()).toBe(true);
  const delivery = await (await page.request.get(`/api/admin/orders/${order.id}/delivery`)).json();
  expect((await page.request.post('/api/client/licenses/activate', { data: { appCode: app.code, licenseKey: delivery.licenses[0].key, deviceId: randomUUID() } })).status()).toBe(200);
  for (const [route, endpoint] of [['licenses', '/api/admin/licenses'], ['batches', '/api/admin/batch-overview'], ['reports', '/api/admin/reports/operations'], ['expiring', '/api/admin/expiring-licenses']]) {
    await page.goto(`/${route}`);
    await page.getByLabel('所属渠道', { exact: true }).fill(name);
    const filtered = page.waitForResponse(response => {
      const url = new URL(response.url());
      return url.pathname === endpoint && url.searchParams.get('channelId') === channel.id;
    });
    await page.getByTitle(name, { exact: true }).click();
    const response = await filtered;
    expect(response.status()).toBe(200);
    const result = await response.json();
    if (route === 'reports') {
      expect(result.totals.issued).toBe(1);
      await expect(page.getByTestId('report-issued')).toHaveText('1');
    } else {
      expect(result.total).toBe(1);
      await expect(page.locator('.count-badge').first()).toHaveText('1');
    }
  }
});
