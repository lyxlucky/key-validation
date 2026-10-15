import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Alert, App, Button, Form, Input, QRCode, Space, Tag } from 'antd';
import { CheckCircleOutlined, ClockCircleOutlined, CopyOutlined, DownloadOutlined, KeyOutlined, LockOutlined, SafetyCertificateOutlined, UserOutlined } from '@ant-design/icons';
import { api } from '../../lib/api';
import type { Dto } from '../../lib/types';
import { PageTitle, QueryError } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';
import { LiveModal } from '../../components/LiveModal';
import { CopyValue, copyText } from '../../components/CopyValue';
import { downloadText } from '../../lib/download';
import './security.css';

type SecurityStatus = Dto<'AccountSecurityDto'>;
type Editor = 'password' | 'setup' | 'confirm' | 'disable' | 'recovery';
const titles: Record<Editor, string> = { password: '修改密码', setup: '开启双重验证', confirm: '验证认证器', disable: '关闭双重验证', recovery: '重新生成恢复码' };
const descriptions: Record<Editor, string> = {
  password: '设置一个新的强密码。本次登录将保留，其他设备需要重新登录。',
  setup: '先确认当前密码，再将账号添加到你的认证器。',
  confirm: '使用认证器扫描二维码，然后输入验证码完成绑定。',
  disable: '确认当前密码后关闭。下次登录将只验证账号密码，现有恢复码也会失效。',
  recovery: '确认当前密码后生成一组新的恢复码，请替换你保存的旧恢复码。',
};

export function SecurityPage() {
  const auth = useAuth();
  const [form] = Form.useForm();
  const { message } = App.useApp();
  const status = useQuery({ queryKey: ['security'], queryFn: () => api<SecurityStatus>('/admin/security') });
  const [editor, setEditor] = useState<Editor | null>(null);
  const [setup, setSetup] = useState<{ sharedKey: string; authenticatorUri: string } | null>(null);
  const [codes, setCodes] = useState<string[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  function open(next: Editor) { form.resetFields(); setEditor(next); setError(null); setSetup(null); }
  function close() { if (!busy) { form.resetFields(); setEditor(null); setSetup(null); setError(null); } }
  async function save(values: { currentPassword: string; newPassword: string; password: string; code: string }) {
    if (!editor || busy) return;
    setBusy(true); setError(null);
    try {
      if (editor === 'setup') {
        const next = await api<{ sharedKey: string; authenticatorUri: string }>('/admin/security/two-factor/setup', { method: 'POST', body: { password: values.password } });
        form.resetFields(); setSetup(next); setEditor('confirm'); return;
      }
      const code = values.code?.trim();
      if (editor === 'password') await api('/admin/security/password', { method: 'POST', body: { currentPassword: values.currentPassword, newPassword: values.newPassword } });
      if (editor === 'confirm') {
        const result = await api<{ recoveryCodes: string[] }>('/admin/security/two-factor/confirm', { method: 'POST', body: { code } });
        setCodes(result.recoveryCodes);
      }
      if (editor === 'disable') await api('/admin/security/two-factor/disable', { method: 'POST', body: { password: values.password } });
      if (editor === 'recovery') {
        const result = await api<{ recoveryCodes: string[] }>('/admin/security/two-factor/recovery-codes', { method: 'POST', body: { password: values.password } });
        setCodes(result.recoveryCodes);
      }
      form.resetFields(); setEditor(null); setSetup(null);
      await auth.refreshUser();
      message.success('账号安全设置已更新');
    } catch (caught) { setError((caught as Error).message); }
    finally { setBusy(false); }
  }
  const enabled = status.data?.twoFactorEnabled ?? auth.user?.twoFactorEnabled ?? false;
  const settingsDisabled = busy || !!auth.user?.mustChangePassword || status.isPending || status.isError;
  return <div className="security-page"><PageTitle title="账号安全" subtitle="管理登录方式，保护你的账号与工作空间。" /><QueryError error={status.error} retry={status.refetch} />
    {auth.user?.mustChangePassword && <Alert type="warning" showIcon title="请先修改初始密码" description="当前账号需要更新密码后才能访问其他工作台功能。" className="query-error" />}
    <section className={`security-summary${enabled ? ' is-protected' : ''}`} aria-label="登录保护概况">
      <span className="security-summary-icon" aria-hidden="true"><SafetyCertificateOutlined /></span>
      <div className="security-summary-copy">
        <span className="security-eyebrow">登录保护</span>
        <h2>{enabled ? '双重验证已开启' : '为账号添加一层登录保护'}</h2>
        <p>{enabled ? '登录时确认身份，本次会话内无需重复输入验证码。' : '在密码之外，用认证器确认登录身份，让工作空间更安全。'}</p>
      </div>
      <div className="security-account"><UserOutlined aria-hidden /><div><span>当前账号</span><strong title={auth.user?.userName}>{auth.user?.userName}</strong></div></div>
    </section>
    <div className="security-layout">
      <section className="security-settings" aria-labelledby="security-settings-heading" aria-busy={status.isPending}>
        <div className="security-section-heading"><h2 id="security-settings-heading">登录与恢复方式</h2><p>管理用于确认你身份的验证方式。</p></div>
        <div className="security-setting">
          <span className="security-setting-icon" aria-hidden="true"><LockOutlined /></span>
          <div className="security-setting-copy"><div className="security-setting-title"><h3>登录密码</h3><Tag bordered={false} color={auth.user?.mustChangePassword ? 'warning' : 'default'}>{auth.user?.mustChangePassword ? '待修改' : '已设置'}</Tag></div><p>使用独立的强密码。修改后，其他设备会退出登录。</p></div>
          <Button className="security-setting-action" type={auth.user?.mustChangePassword ? 'primary' : 'default'} disabled={busy} onClick={() => open('password')}>修改密码</Button>
        </div>
        <div className="security-setting">
          <span className="security-setting-icon is-blue" aria-hidden="true"><SafetyCertificateOutlined /></span>
          <div className="security-setting-copy"><div className="security-setting-title"><h3>双重验证</h3><Tag bordered={false} icon={enabled ? <CheckCircleOutlined /> : undefined} color={enabled ? 'success' : 'default'}>{enabled ? '已开启' : '未开启'}</Tag></div><p>登录时使用认证器验证码，也可以使用一次性恢复码。</p></div>
          {enabled ? <Button className="security-setting-action" danger disabled={settingsDisabled} onClick={() => open('disable')}>关闭双重验证</Button> : <Button className="security-setting-action" type="primary" disabled={settingsDisabled} onClick={() => open('setup')}>开启双重验证</Button>}
        </div>
        <div className="security-setting">
          <span className="security-setting-icon" aria-hidden="true"><KeyOutlined /></span>
          <div className="security-setting-copy"><div className="security-setting-title"><h3>恢复码</h3>{enabled && <span className={`security-code-count${status.data?.recoveryCodesRemaining === 0 ? ' is-empty' : ''}`}><strong>{status.data?.recoveryCodesRemaining ?? '—'}</strong> 个可用</span>}</div><p>{enabled ? '无法使用认证器时，用恢复码登录。每个码只能使用一次。' : '开启双重验证后可获得恢复码，请保存在安全的位置。'}</p></div>
          <Button className="security-setting-action" disabled={settingsDisabled || !enabled} onClick={() => open('recovery')}>重新生成恢复码</Button>
        </div>
        <div className="security-settings-footer"><LockOutlined aria-hidden /><span>安全设置仅对当前账号生效。</span></div>
      </section>
      <aside className="security-guide" aria-labelledby="security-guide-heading">
        <div className="security-guide-heading"><SafetyCertificateOutlined aria-hidden /><h2 id="security-guide-heading">登录验证流程</h2></div>
        <ol className="security-login-steps">
          <li><span aria-hidden="true">1</span><div><h3>输入账号密码</h3><p>使用你的用户名与登录密码。</p></div></li>
          <li><span aria-hidden="true">2</span><div><h3>{enabled ? '完成双重验证' : '开启后验证身份'}</h3><p>输入认证器验证码，或使用恢复码。</p></div></li>
          <li><span aria-hidden="true">3</span><div><h3>进入工作台</h3><p>连续处理业务，无需反复验证。</p></div></li>
        </ol>
        <div className="security-guide-note"><ClockCircleOutlined aria-hidden /><p>会话结束后，下次登录时重新验证。</p></div>
      </aside>
    </div>
    <LiveModal className="security-dialog" title={editor ? titles[editor] : ''} open={!!editor} onCancel={close} closable={{ 'aria-label': '关闭', disabled: busy }} maskClosable={!busy} cancelButtonProps={{ disabled: busy }} confirmLoading={busy} okText={editor === 'confirm' ? '确认开启' : '保存修改'} cancelText="取消" okButtonProps={{ htmlType: 'submit', form: 'security-editor-form', danger: editor === 'disable' }}>
      {editor && <p className="security-dialog-description">{descriptions[editor]}</p>}
      {(editor === 'setup' || editor === 'confirm') && <div className="security-setup-steps" aria-label="绑定步骤"><span className={editor === 'setup' ? 'is-current' : 'is-complete'}><b>{editor === 'confirm' ? <CheckCircleOutlined aria-hidden /> : '1'}</b>确认密码</span><i aria-hidden /><span className={editor === 'confirm' ? 'is-current' : ''}><b>2</b>绑定认证器</span></div>}
      {error && <Alert type="error" showIcon title={error} className="query-error" />}
      {setup && <div className="authenticator-setup"><QRCode value={setup.authenticatorUri} size={168} /><div><p>无法扫描时，手动输入密钥</p><div data-testid="authenticator-key"><CopyValue value={setup.sharedKey} label="认证器密钥" /></div><span>请在 5 分钟内完成绑定。</span></div></div>}
      {editor === 'recovery' && <Alert type="warning" showIcon title="保存后所有旧恢复码立即失效" className="query-error" />}
      {editor && <Form form={form} key={editor} id="security-editor-form" name={`security-${editor}`} layout="vertical" requiredMark={false} disabled={busy} onFinish={save}>
        {editor === 'password' ? <><Form.Item name="currentPassword" label="当前密码" rules={[{ required: true, message: '请输入当前密码' }]}><Input.Password autoComplete="current-password" maxLength={1024} /></Form.Item><Form.Item name="newPassword" label="新密码" rules={[{ required: true, message: '请输入新密码' }, { min: 12, message: '至少 12 位' }, { pattern: /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).+$/, message: '需包含大小写字母、数字和符号' }]} extra="至少 12 位，包含大小写字母、数字和符号，不能与旧密码相同。"><Input.Password autoComplete="new-password" maxLength={1024} /></Form.Item><Form.Item name="confirmPassword" label="确认新密码" dependencies={['newPassword']} rules={[{ required: true, message: '请再次输入新密码' }, ({ getFieldValue }) => ({ validator: (_, value) => value === getFieldValue('newPassword') ? Promise.resolve() : Promise.reject(new Error('两次密码不一致')) })]}><Input.Password autoComplete="new-password" maxLength={1024} /></Form.Item></> : <>
          {['setup', 'disable', 'recovery'].includes(editor) && <Form.Item name="password" label="当前密码" rules={[{ required: true, message: '请输入当前密码' }]}><Input.Password autoComplete="current-password" maxLength={1024} /></Form.Item>}
          {editor === 'confirm' && <><Form.Item name="code" label="验证码" rules={[{ required: true, message: '请输入认证器中的验证码' }, { pattern: /^\d{6}$/, message: '请输入 6 位数字验证码' }]}><Input className="security-totp-input" inputMode="numeric" autoComplete="one-time-code" maxLength={6} placeholder="6 位验证码" /></Form.Item><p className="form-note">验证码只能使用一次。刚使用过的验证码需要等待更新。</p></>}
        </>}
      </Form>}
    </LiveModal>
    <LiveModal className="security-dialog" title="保存恢复码" open={!!codes} closable={false} maskClosable={false} keyboard={false} footer={<Space wrap>
      <Button icon={<DownloadOutlined aria-hidden />} onClick={() => { if (codes) downloadText(codes.join('\n'), 'keygate-recovery-codes.txt'); }}>下载恢复码</Button>
      <Button icon={<CopyOutlined aria-hidden />} onClick={async () => { if (!codes) return; try { await copyText(codes.join('\n')); message.success('恢复码已复制，请妥善保存'); } catch { message.error('复制失败，请下载保存'); } }}>复制全部</Button>
      <Button type="primary" onClick={() => setCodes(null)}>已安全保存，关闭</Button>
    </Space>}>
      <Alert type="warning" showIcon title="恢复码仅在此显示一次" description="每个恢复码可替代认证器完成一次验证。请妥善保存，关闭后无法再次查看。" className="query-error" />
      <div className="recovery-codes">{codes?.map(code => <code key={code} data-testid="recovery-code">{code}</code>)}</div>
    </LiveModal>
  </div>;
}
