import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { syntheticAdmin, fulfill } from './review-lifecycle-helpers';

test.use({ permissions: ['clipboard-read', 'clipboard-write'] });
const doc = { id: 'quick-start', title: '共享快速开始', topic: 'quick-start', version: '2026-10-11', markdown: '# 激活流程\n使用 `appCode`。\n\n```http\nPOST /api/client/licenses/activate\n```\n<script>window.unsafeMarkdown=true</script>' };
async function setup(page: Page, failed = false) {
  const fixture = await syntheticAdmin(page, { permissions: ['integrations.manage'] });
  await page.route('**/api/integrations/developer-docs**', route => {
    if (failed) return fulfill(route, { code: 'UNAVAILABLE' }, 503);
    return fulfill(route, new URL(route.request().url()).pathname.endsWith('/quick-start') ? doc : [{ id: doc.id, title: doc.title, topic: doc.topic, version: doc.version }]);
  });
  await page.goto('/integrations');
  return { fixture, recover: () => { failed = false; } };
}
test('MCP config and prompts copy the current origin without business requests', async ({ page }) => {
  const { fixture } = await setup(page);
  await page.getByRole('tab', { name: 'MCP 接入', exact: true }).click();
  await expect(page.getByTestId('mcp-url')).toHaveText('http://127.0.0.1:5173/mcp');
  await page.getByRole('button', { name: '复制 VS Code 配置', exact: true }).click();
  const config = JSON.parse(await page.evaluate(() => navigator.clipboard.readText()));
  expect(config).toEqual({ servers: { keygate: { type: 'http', url: 'http://127.0.0.1:5173/mcp' } } });
  await expect(page.getByText('.vscode/mcp.json', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: '复制 Claude Code 命令', exact: true }).click();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe('claude mcp add --transport http --scope project keygate "http://127.0.0.1:5173/mcp"');
  await page.getByRole('button', { name: '复制提问 1', exact: true }).click();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toContain('Python');
  expect(fixture.state.requests.filter(r => !r.path.startsWith('/api/admin/auth/'))).toEqual([]);
});
test('shared docs retry, safely render, copy code and preserve version after reload', async ({ page }) => {
  const { recover } = await setup(page, true);
  await page.getByRole('button', { name: '重试', exact: true }).click({ trial: true });
  recover();
  await page.getByRole('button', { name: '重试', exact: true }).click();
  await expect(page.getByText('资料版本：2026-10-11', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '激活流程', exact: true })).toBeVisible();
  expect(await page.evaluate(() => (window as unknown as { unsafeMarkdown?: boolean }).unsafeMarkdown)).toBeUndefined();
  await page.getByRole('button', { name: '复制代码块 1', exact: true }).click();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe('POST /api/client/licenses/activate');
  await page.reload();
  await expect(page.getByText('资料版本：2026-10-11', { exact: true })).toBeVisible();
  for (const title of ['商家服务端接入业务 API', '让客户使用自助服务', '接收 Webhook 业务事件', '服务端离线签名配置与密钥轮换']) await expect(page.getByText(title, { exact: true })).toBeVisible();
});
