import { test, expect } from '@playwright/test';
import { closeAndInspect, enter, hiddenPanel, logoutAndLogin, openCreateAccount, openPasswordEditor, syntheticAdmin, syntheticCode, syntheticKey, syntheticPassword, syntheticSharedKey, visitSystem } from './review-lifecycle-helpers';

test.use({ trace: 'off', screenshot: 'off', reducedMotion: 'reduce', serviceWorkers: 'block' });

for (const kind of ['create', 'role', 'password'] as const) {
  test(`the account ${kind} draft survives Activity hiding and is discarded on actual close`, async ({ page }) => {
    await syntheticAdmin(page);
    await enter(page);
    await page.getByRole('menuitem', { name: '账号管理', exact: true }).click();
    const open = async () => {
      if (kind === 'create') await page.getByRole('button', { name: '创建账号', exact: true }).click();
      else await page.getByRole('button', { name: kind === 'role' ? '调整角色' : '重置密码', exact: true }).click();
    };
    const fill = async () => {
      if (kind === 'create') await page.locator('#account-editor_userName').fill('review-retained-draft');
      if (kind !== 'role') await page.locator('#account-editor_password').fill(syntheticPassword);
      if (kind !== 'password') {
        await page.locator('#account-editor_roleIds').click();
        await page.getByTitle('Review role two', { exact: true }).click();
        await page.locator('#account-editor_roleIds').press('Escape');
      }
    };
    await open();
    await fill();
    await page.goBack();
    await expect(page.locator('#account-editor-form')).toBeHidden();
    await hiddenPanel(page, '账号管理');
    await page.getByRole('tab', { name: '账号管理', exact: true }).click();
    if (kind === 'create') await expect(page.locator('#account-editor_userName')).toHaveValue('review-retained-draft');
    if (kind !== 'role') await expect(page.locator('#account-editor_password')).toHaveValue(syntheticPassword);
    if (kind !== 'password') await expect(page.locator('#account-editor-form .ant-select-selection-item').filter({ hasText: 'Review role two' })).toBeVisible();
    const dialog = page.getByRole('dialog');
    const closed = await closeAndInspect(dialog.getByRole('button', { name: '取消', exact: true }), '#account-editor-form', syntheticPassword);
    expect(closed.nodes).toBe(0);
    await open();
    if (kind === 'create') await expect(page.locator('#account-editor_userName')).toHaveValue('');
    if (kind !== 'role') await expect(page.locator('#account-editor_password')).toHaveValue('');
    if (kind !== 'password') await expect(page.locator('#account-editor-form .ant-select-selection-item').filter({ hasText: 'Review role two' })).toHaveCount(0);
    await fill();
    await page.goBack();
    await expect(page.locator('#account-editor-form')).toBeHidden();
    await page.getByRole('button', { name: '关闭账号管理标签', exact: true }).click();
    await expect(page.locator('#account-editor-form')).toHaveCount(0);
    await page.getByRole('menuitem', { name: '账号管理', exact: true }).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await open();
    if (kind === 'create') await expect(page.locator('#account-editor_userName')).toHaveValue('');
    if (kind !== 'role') await expect(page.locator('#account-editor_password')).toHaveValue('');
  });
}

const securityEditors = [
  { kind: 'password', title: '修改密码', fields: ['currentPassword', 'newPassword', 'confirmPassword'] },
  { kind: 'setup', title: '开启双重验证', fields: ['password'] },
  { kind: 'confirm', title: '验证认证器', fields: ['code'] },
  { kind: 'disable', title: '关闭双重验证', fields: ['password'] },
  { kind: 'recovery', title: '重新生成恢复码', fields: ['password'] },
];

for (const editor of securityEditors) {
  test(`the security ${editor.kind} draft survives Activity hiding and is discarded on actual close`, async ({ page }) => {
    await syntheticAdmin(page, { mfa: !['setup', 'confirm'].includes(editor.kind) });
    await enter(page, '/accounts');
    await page.getByRole('menuitem', { name: '账号安全', exact: true }).click();
    const open = async () => {
      await page.getByRole('button', { name: editor.kind === 'confirm' ? '开启双重验证' : editor.title, exact: true }).click();
      if (editor.kind === 'confirm') {
        await page.locator('#security-setup_password').fill(syntheticPassword);
        await page.locator('button[form="security-editor-form"]').click();
        await expect(page.getByTestId('authenticator-key')).toBeVisible();
      }
    };
    const input = (field: string) => page.locator(`#security-${editor.kind}_${field}`);
    await open();
    for (const field of editor.fields) await input(field).fill(field === 'code' ? '654321' : syntheticPassword);
    await page.goBack();
    await expect(page.locator('#security-editor-form')).toBeHidden();
    await hiddenPanel(page, '账号安全');
    await page.getByRole('tab', { name: '账号安全', exact: true }).click();
    for (const field of editor.fields) await expect(input(field)).toHaveValue(field === 'code' ? '654321' : syntheticPassword);
    const closed = await closeAndInspect(page.getByRole('dialog', { name: editor.title, exact: true }).getByRole('button', { name: '取消', exact: true }), '#security-editor-form', syntheticPassword);
    expect(closed.nodes).toBe(0);
    await open();
    for (const field of editor.fields) await expect(input(field)).toHaveValue('');
    await page.goBack();
    await expect(page.locator('#security-editor-form')).toBeHidden();
    await page.getByRole('button', { name: '关闭账号安全标签', exact: true }).click();
    await expect(page.locator('#security-editor-form')).toHaveCount(0);
    await expect(page.getByTestId('authenticator-key')).toHaveCount(0);
    await page.getByRole('menuitem', { name: '账号安全', exact: true }).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
  });
}

for (const operation of [
  { title: '关闭双重验证', path: 'disable' },
  { title: '重新生成恢复码', path: 'recovery-codes' },
]) {
  test(`${operation.title} uses the current password without requesting another second factor`, async ({ page }) => {
    await syntheticAdmin(page);
    await enter(page);
    await page.getByRole('button', { name: operation.title, exact: true }).click();
    const dialog = page.getByRole('dialog', { name: operation.title, exact: true });
    await expect(dialog.getByLabel('验证码', { exact: true })).toHaveCount(0);
    await expect(dialog.getByRole('radio')).toHaveCount(0);
    await dialog.getByLabel('当前密码', { exact: true }).fill(syntheticPassword);
    const submitted = page.waitForRequest(request => request.url().endsWith(`/api/admin/security/two-factor/${operation.path}`) && request.method() === 'POST');
    await dialog.getByRole('button', { name: '保存修改', exact: true }).click();
    expect((await submitted).postDataJSON()).toEqual({ password: syntheticPassword });
    if (operation.path === 'disable') await expect(page.getByText('未开启', { exact: true })).toBeVisible();
    else await expect(page.getByTestId('recovery-code')).toBeVisible();
  });
}

test('logout and account change release hidden account and security drafts', async ({ page }) => {
  await syntheticAdmin(page);
  await enter(page);
  await visitSystem(page, '账号管理');
  await openCreateAccount(page);
  await page.goBack();
  await expect(page.locator('#account-editor-form')).toBeHidden();
  await openPasswordEditor(page);
  await page.goForward();
  await expect(page.locator('#security-editor-form')).toBeHidden();
  // Both retained editors are open; close only the active one to reach logout.
  await page.getByRole('dialog', { name: '创建账号', exact: true }).getByRole('button', { name: '取消', exact: true }).click();
  await logoutAndLogin(page);
  await expect(page.locator('#security-editor-form, #account-editor-form')).toHaveCount(0);
  await visitSystem(page, '账号管理');
  await page.getByRole('button', { name: '创建账号', exact: true }).click();
  await expect(page.locator('#account-editor_userName')).toHaveValue('');
  await expect(page.locator('#account-editor_password')).toHaveValue('');
  await page.goBack();
  await expect(page.getByRole('menuitem', { name: '系统管理', exact: true })).toHaveAttribute('aria-expanded', 'false');
  await visitSystem(page, '账号安全');
  await expect(page.getByRole('dialog')).toHaveCount(0);
});

test('permission revocation discards the retained account editor', async ({ page }) => {
  const fixture = await syntheticAdmin(page);
  await enter(page);
  await page.getByRole('menuitem', { name: '账号管理', exact: true }).click();
  await openCreateAccount(page);
  await page.goBack();
  await expect(page.locator('#account-editor-form')).toBeHidden();
  fixture.state.permissions = [];
  await openPasswordEditor(page);
  await page.locator('button[form="security-editor-form"]').click();
  await expect(page.getByRole('tab', { name: '账号管理', exact: true })).toHaveCount(0);
  await expect(page.locator('#account-editor-form')).toHaveCount(0);
  await expect(page.getByRole('menuitem', { name: '账号管理', exact: true })).toHaveCount(0);
});

for (const reducedMotion of ['no-preference', 'reduce'] as const) {
  test(`recovery codes leave the DOM at close commit with ${reducedMotion} motion`, async ({ page }) => {
    await page.emulateMedia({ reducedMotion });
    await syntheticAdmin(page);
    await enter(page);
    await page.getByRole('button', { name: '重新生成恢复码', exact: true }).click();
    await page.locator('#security-recovery_password').fill(syntheticPassword);
    await page.locator('button[form="security-editor-form"]').click();
    await expect(page.getByTestId('recovery-code')).toBeVisible();
    const closed = await closeAndInspect(page.getByRole('button', { name: '已安全保存，关闭', exact: true }), '[data-testid="recovery-code"]', syntheticCode);
    expect(closed.nodes).toBe(0);
    expect(closed.plaintext).toBe(false);
    if (reducedMotion === 'no-preference') expect(closed.shellRetained).toBe(true);
    await expect(page.getByRole('dialog', { name: '保存恢复码', exact: true })).toBeHidden();
    await expect(page.getByTestId('recovery-code')).toHaveCount(0);
  });

  test(`setup key and QR leave the DOM at every dismiss path with ${reducedMotion} motion`, async ({ page }) => {
    await page.emulateMedia({ reducedMotion });
    await syntheticAdmin(page, { mfa: false });
    await enter(page);
    for (const action of ['cancel', 'close', 'escape', 'mask']) {
      await page.getByRole('button', { name: '开启双重验证', exact: true }).click();
      await page.locator('#security-setup_password').fill(syntheticPassword);
      await page.locator('button[form="security-editor-form"]').click();
      const dialog = page.getByRole('dialog', { name: '验证认证器', exact: true });
      await expect(page.getByTestId('authenticator-key')).toBeVisible();
      await expect(dialog.locator('.ant-qrcode')).toBeVisible();
      if (action === 'cancel' || action === 'close') {
        const closed = await closeAndInspect(dialog.getByRole('button', { name: action === 'cancel' ? '取消' : '关闭', exact: true }), '.authenticator-setup', syntheticSharedKey);
        expect(closed.nodes).toBe(0);
        expect(closed.plaintext).toBe(false);
        if (reducedMotion === 'no-preference') expect(closed.shellRetained).toBe(true);
      } else {
        if (action === 'escape') await page.keyboard.press('Escape');
        else await page.locator('.ant-modal-wrap').filter({ visible: true }).click({ position: { x: 8, y: 8 } });
        expect(await page.locator('.authenticator-setup').count(), action).toBe(0);
        expect(await page.locator('body').evaluate(body => body.textContent?.includes('SYNTHETICREVIEWKEY'))).toBe(false);
      }
      await expect(dialog).toBeHidden();
      await expect(page.locator('.authenticator-setup, [data-testid="authenticator-key"]')).toHaveCount(0);
    }
  });

  test(`issued keys leave the DOM at close commit with ${reducedMotion} motion`, async ({ page }) => {
    await page.emulateMedia({ reducedMotion });
    await syntheticAdmin(page, { permissions: ['licenses.read', 'licenses.issue'] });
    await enter(page, '/licenses');
    await page.getByRole('button', { name: /生成卡密/ }).click();
    await page.locator('#issue-licenses_applicationId').click();
    await expect(page.getByTitle('Review application', { exact: true })).toBeVisible();
    await page.locator('#issue-licenses_applicationId').press('ArrowDown');
    await page.locator('#issue-licenses_applicationId').press('Enter');
    await page.locator('#issue-licenses_planId').click();
    await expect(page.getByTitle('Review plan', { exact: true })).toBeVisible();
    await page.locator('#issue-licenses_planId').press('ArrowDown');
    await page.locator('#issue-licenses_planId').press('Enter');
    await page.locator('#issue-licenses_quantity').fill('1');
    await page.getByRole('button', { name: '确认生成', exact: true }).click();
    await expect(page.getByTestId('issued-key')).toBeVisible();
    // Complete the current save flow before testing plaintext removal at close commit.
    await Promise.all([
      page.waitForEvent('download'),
      page.getByRole('button', { name: '下载 CSV', exact: true }).click(),
    ]);
    const closed = await closeAndInspect(page.getByRole('button', { name: '已保存，关闭', exact: true }), '[data-testid="issued-key"]', syntheticKey);
    expect(closed.nodes).toBe(0);
    expect(closed.plaintext).toBe(false);
    if (reducedMotion === 'no-preference') expect(closed.shellRetained).toBe(true);
    await expect(page.getByRole('dialog', { name: '卡密已生成', exact: true })).toBeHidden();
    await expect(page.getByTestId('issued-key')).toHaveCount(0);
  });
}

for (const history of ['never-opened', 'closed']) {
  test(`a ${history} shared secret dialog cannot intercept a retained editor's Escape key`, async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'no-preference' });
    await syntheticAdmin(page, { permissions: ['customers.manage'] });
    await enter(page);
    await page.getByRole('menuitem', { name: '客户与渠道', exact: true }).click();
    await page.getByRole('menuitem', { name: '客户管理', exact: true }).click();
    if (history === 'closed') {
      await page.getByRole('button', { name: '访问码', exact: true }).click();
      await page.getByRole('button', { name: '生成新访问码', exact: true }).click();
      await expect(page.getByTestId('one-time-secret')).toBeVisible();
      const closed = await closeAndInspect(page.getByRole('button', { name: '已保存，关闭', exact: true }), '[data-testid="one-time-secret"]', syntheticCode);
      expect(closed.nodes).toBe(0);
      expect(closed.plaintext).toBe(false);
      expect(closed.shellRetained).toBe(true);
      await expect(page.getByRole('dialog', { name: '保存客户访问码', exact: true })).toBeHidden();
    }
    await page.getByRole('button', { name: '新建客户', exact: true }).click();
    await page.locator('#customer-editor_name').fill('review-customer-draft');
    await page.goBack();
    await hiddenPanel(page, '客户管理');
    await page.getByRole('tab', { name: '客户管理', exact: true }).click();
    const editor = page.getByRole('dialog', { name: '新建客户', exact: true });
    await expect(editor).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(editor).toBeHidden();
    await expect(page.getByTestId('one-time-secret')).toHaveCount(0);
  });
}
