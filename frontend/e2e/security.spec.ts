import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { createHmac, randomUUID } from 'node:crypto';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

// Credentials, enrollment secrets and recovery codes must not enter trace artifacts.
test.use({ trace: 'off', screenshot: 'off' });
function totp(secret: string, step = Math.floor(Date.now() / 30000)) {
  let bits = '';
  for (const char of secret.toUpperCase().replace(/\s/g, '')) bits += 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'.indexOf(char).toString(2).padStart(5, '0');
  const bytes = Buffer.from((bits.match(/.{8}/g) ?? []).map(byte => parseInt(byte, 2)));
  const counter = Buffer.alloc(8); counter.writeBigUInt64BE(BigInt(step));
  const hash = createHmac('sha1', bytes).update(counter).digest();
  return String((hash.readUInt32BE(hash[19] & 15) & 0x7fffffff) % 1000000).padStart(6, '0');
}
async function login(page: Page, name = process.env.E2E_ADMIN_USER!, password = process.env.E2E_ADMIN_PASSWORD!) {
  await page.goto('/login');
  await page.getByLabel('用户名', { exact: true }).fill(name);
  await page.getByLabel('密码', { exact: true }).fill(password);
  await page.getByRole('button', { name: '登录', exact: true }).click();
}
async function headers(page: Page) { return { 'X-CSRF-TOKEN': (await (await page.request.get('/api/admin/auth/csrf')).json()).token }; }

test('first password change and authenticator recovery lifecycle', async ({ page, browser }) => {
  await login(page);
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
  const name = `security-${randomUUID().slice(0, 8)}`;
  const password = `${randomUUID()}Aa1!`;
  const changed = `${password}Changed`;
  expect((await page.request.post('/api/admin/accounts', { headers: await headers(page), data: { userName: name, password, role: 'Operator' } })).status()).toBe(201);
  const context = await browser.newContext({ baseURL: 'http://127.0.0.1:5173', viewport: { width: 375, height: 812 } });
  try {
    const account = await context.newPage();
    await login(account, name, password);
    await expect(account.getByRole('heading', { name: '账号安全', exact: true })).toBeVisible();
    expect((await account.request.get('/api/admin/licenses')).status()).toBe(403);
    await account.goto('/licenses');
    await expect(account).toHaveURL(/\/security$/);
    await account.getByRole('button', { name: '修改密码', exact: true }).click();
    const change = account.getByRole('dialog', { name: '修改密码', exact: true });
    await change.getByLabel('当前密码', { exact: true }).fill(password);
    await change.getByLabel('新密码', { exact: true }).fill(changed);
    await change.getByLabel('确认新密码', { exact: true }).fill(changed);
    await change.getByRole('button', { name: '保存修改' }).click();
    await expect(account.getByText('请先修改初始密码', { exact: true })).toHaveCount(0);
    await account.getByRole('button', { name: '开启双重验证', exact: true }).click();
    const setup = account.getByRole('dialog', { name: '开启双重验证', exact: true });
    await setup.getByLabel('当前密码').fill(changed);
    await setup.getByRole('button', { name: '保存修改' }).click();
    const secret = (await account.getByTestId('authenticator-key').innerText()).trim();
    const confirm = account.getByRole('dialog', { name: '验证认证器', exact: true });
    // A previous-step code can expire while the click crosses a clock boundary.
    // Enroll with the current step, then wait for a fresh step before MFA login.
    const enrollmentStep = Math.floor(Date.now() / 30000);
    await confirm.getByLabel('验证码', { exact: true }).fill(totp(secret, enrollmentStep));
    await confirm.getByRole('button', { name: '确认开启' }).click();
    await expect(account.getByTestId('recovery-code')).toHaveCount(10);
    const codes = await account.getByTestId('recovery-code').allTextContents();
    await expect(account.getByRole('button', { name: '已安全保存，关闭' })).toBeInViewport();
    await account.getByRole('button', { name: '已安全保存，关闭' }).click();
    await account.getByRole('button', { name: '退出登录' }).click();
    await expect(account.getByLabel('用户名', { exact: true })).toBeVisible();
    await login(account, name, changed);
    await expect(account.getByText('完成双重验证', { exact: true })).toBeVisible();
    await expect(account.getByLabel('密码', { exact: true })).toHaveCount(0);
    expect((await account.request.get('/api/admin/licenses')).status()).toBe(401);
    await expect.poll(() => Math.floor(Date.now() / 30000), { timeout: 35000, intervals: [250] }).toBeGreaterThan(enrollmentStep);
    await account.getByLabel('验证码', { exact: true }).fill(totp(secret));
    await account.getByRole('button', { name: '验证并登录' }).click();
    await expect(account.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
    await account.getByRole('button', { name: '退出登录' }).click();
    await expect(account.getByLabel('用户名', { exact: true })).toBeVisible();
    await login(account, name, changed);
    await account.getByRole('radio', { name: '恢复码', exact: true }).check();
    await account.getByRole('textbox', { name: '恢复码', exact: true }).fill(codes[0]);
    await account.getByRole('button', { name: '验证并登录' }).click();
    await expect(account.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
    await account.goto('/security');
    await expect(account.getByText('9', { exact: true })).toBeVisible();
    await expect(account.getByRole('button', { name: '再次验证身份', exact: true })).toHaveCount(0);
    await account.getByRole('button', { name: '关闭双重验证', exact: true }).click();
    const disable = account.getByRole('dialog', { name: '关闭双重验证', exact: true });
    await disable.getByLabel('当前密码').fill(changed);
    await expect(disable.getByLabel('验证码', { exact: true })).toHaveCount(0);
    await expect(disable.getByRole('radio')).toHaveCount(0);
    await disable.getByRole('button', { name: '保存修改' }).click();
    await expect(account.getByText('未开启', { exact: true })).toBeVisible();
    expect(await account.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
    const preview = path.resolve('..', '.local', 'security-preview');
    await mkdir(preview, { recursive: true });
    await account.screenshot({ path: path.join(preview, 'security-375.png'), fullPage: true, animations: 'disabled' });
  } finally { await context.close(); }
});

test('device policy edits and resets update the license drawer', async ({ page }) => {
  await login(page);
  await expect(page.getByRole('heading', { name: '概览', exact: true })).toBeVisible();
  const adminHeaders = await headers(page);
  const app = await (await page.request.post('/api/admin/applications', { headers: adminHeaders, data: { name: '换绑验收', code: `rebind-${randomUUID().slice(0, 8)}` } })).json();
  const plan = await (await page.request.post(`/api/admin/applications/${app.id}/plans`, { headers: adminHeaders, data: { name: '换绑日卡', durationSeconds: 86400, maxDevices: 1 } })).json();
  const issuance = await (await page.request.post('/api/admin/license-batches', { headers: adminHeaders, data: { applicationId: app.id, planId: plan.id, quantity: 1, requestId: randomUUID() } })).json();
  const license = issuance.licenses[0];
  const session = await (await page.request.post('/api/client/licenses/activate', { data: { appCode: app.code, licenseKey: license.key, deviceId: randomUUID() } })).json();
  await page.goto(`/licenses?batchId=${issuance.batch.id}`);
  await page.getByRole('row').filter({ hasText: license.keyHint }).getByRole('button', { name: '详情' }).click();
  await page.getByRole('button', { name: '设置换绑规则', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '设置换绑规则', exact: true });
  await editor.getByLabel('窗口内最多次数').fill('2');
  await editor.getByLabel('调整原因').fill('售后规则验收');
  await editor.getByRole('button', { name: '保存规则' }).click();
  await expect(page.getByText('滚动 30 天，已用 0 次，剩余 2 次', { exact: true })).toBeVisible();
  expect((await page.request.post('/api/client/device-bindings/release', { headers: { Authorization: `Bearer ${session.sessionToken}` } })).status()).toBe(204);
  await page.getByRole('button', { name: '重置次数与冷却' }).click();
  const reset = page.getByRole('dialog', { name: '重置换绑次数与冷却', exact: true });
  await reset.getByLabel('调整原因').fill('售后重置验收');
  await reset.getByRole('button', { name: '保存规则' }).click();
  await expect(reset).toBeHidden();
  const policy = await (await page.request.get(`/api/admin/licenses/${license.id}/device-policy`)).json();
  expect(policy.usedChanges).toBe(0); expect(policy.nextAllowedAt).toBeNull(); expect(policy.maxChanges).toBe(2);
});
