import { expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';

export async function login(page: Page, userName = process.env.E2E_ADMIN_USER!, password = process.env.E2E_ADMIN_PASSWORD!, firstLogin = false) {
  await page.goto('/login');
  await page.getByLabel('用户名', { exact: true }).fill(userName);
  await page.getByLabel('密码', { exact: true }).fill(password);
  await page.getByRole('button', { name: '登录', exact: true }).click();
  if (firstLogin) {
    await expect(page.getByRole('heading', { name: '账号安全', exact: true })).toBeVisible();
    await page.getByRole('button', { name: '修改密码', exact: true }).click();
    const change = page.getByRole('dialog', { name: '修改密码', exact: true });
    await change.getByLabel('当前密码', { exact: true }).fill(password);
    await change.getByLabel('新密码', { exact: true }).fill(`${password}New`);
    await change.getByLabel('确认新密码', { exact: true }).fill(`${password}New`);
    await change.getByRole('button', { name: '保存修改', exact: true }).click();
    await expect(change).toBeHidden();
    await page.goto('/overview');
  }
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
  return { 'X-CSRF-TOKEN': (await (await page.request.get('/api/admin/auth/csrf')).json()).token };
}

export async function catalog(page: Page, quantity = 2) {
  const headers = await login(page);
  const name = `运营验收-${randomUUID().slice(0, 8)}`;
  const response = await page.request.post('/api/admin/applications', { headers, data: { name, code: `ops-${randomUUID().slice(0, 8)}` } });
  expect(response.status()).toBe(201);
  const app = await response.json();
  const planResponse = await page.request.post(`/api/admin/applications/${app.id}/plans`, { headers, data: { name: '运营日卡', durationSeconds: 86400, maxDevices: 1 } });
  expect(planResponse.status()).toBe(201);
  const plan = await planResponse.json();
  const issued = await page.request.post('/api/admin/license-batches', { headers, data: { applicationId: app.id, planId: plan.id, quantity, requestId: randomUUID(), note: name } });
  expect(issued.status()).toBe(201);
  return { headers, app, plan, issuance: await issued.json() };
}
