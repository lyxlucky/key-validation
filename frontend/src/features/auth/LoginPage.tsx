import { useState } from 'react';
import { Alert, Button, Form, Input, Radio } from 'antd';
import { ArrowRightOutlined, CheckOutlined, DesktopOutlined, KeyOutlined, LockOutlined, SafetyCertificateOutlined, UserOutlined } from '@ant-design/icons';
import { Navigate } from 'react-router-dom';
import { Brand } from '../../components/common';
import { useAuth } from './AuthProvider';

export function LoginPage() {
  const auth = useAuth();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [recovery, setRecovery] = useState(false);
  if (auth.user) return <Navigate to={auth.user.mustChangePassword ? '/security' : '/'} replace />;
  return <div className="login-page screen-enter">
    <section className="login-story">
      <Brand />
      <div className="login-intro"><span className="login-kicker"><span /> SOFTWARE LICENSING PLATFORM</span><h1>每一份授权，<br />都在你的掌握之中。</h1><p>连接软件与用户，让授权管理清晰有序。<br />从卡密发行到设备验证，在这里完成。</p>
        <div className="license-illustration" aria-label="授权流程：发行卡密、绑定设备、验证授权">
          <div className="illustration-heading"><span><SafetyCertificateOutlined /> KEYGATE AUTHORIZATION</span><span className="illustration-dots">•••</span></div>
          <div className="license-flow"><div><span className="flow-icon"><KeyOutlined /></span><strong>发行卡密</strong><small>License</small></div><span className="flow-connector"><ArrowRightOutlined /></span><div><span className="flow-icon"><DesktopOutlined /></span><strong>绑定设备</strong><small>Device</small></div><span className="flow-connector"><ArrowRightOutlined /></span><div><span className="flow-icon flow-icon-verified"><SafetyCertificateOutlined /></span><strong>验证授权</strong><small>Verify</small></div></div>
          <div className="illustration-footer"><span><CheckOutlined /> 完整授权周期</span><span>一个工作台，统一管理</span></div>
        </div>
        <div className="login-benefits"><span><CheckOutlined /> 设备绑定</span><span><CheckOutlined /> 到期控制</span><span><CheckOutlined /> 操作审计</span></div>
      </div>
      <span className="login-footnote">KeyGate <span>·</span> 为软件授权提供清晰、可靠的管理体验</span>
    </section>
    <section className="login-form-side"><div className="login-form-card"><span className="login-form-icon"><LockOutlined /></span><span className="eyebrow">WELCOME BACK</span><h2>登录管理后台</h2><p>欢迎回来，使用你的管理账号继续。</p>
      {(error || auth.error) && <Alert type="error" title={error || auth.error} showIcon className="query-error" />}
      {auth.challenge ? <Form key="two-factor" name="login-two-factor" layout="vertical" requiredMark={false} onFinish={async (values: { code: string }) => { setBusy(true); setError(null); try { await auth.completeLogin(values.code.trim(), recovery); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}>
        <Alert type="info" showIcon title="完成双重验证" description="输入认证器的新验证码，或使用一个尚未使用的恢复码。" className="query-error" />
        <Radio.Group value={recovery} onChange={event => setRecovery(event.target.value)} options={[{ label: '认证器', value: false }, { label: '恢复码', value: true }]} className="query-error" />
        <Form.Item key={String(recovery)} label={recovery ? '恢复码' : '验证码'} name="code" preserve={false} rules={[{ required: true, whitespace: true, message: '请输入验证码或恢复码' }]}><Input size="large" autoComplete="one-time-code" maxLength={100} /></Form.Item>
        <Button type="primary" htmlType="submit" block loading={busy}>验证并登录</Button>
        <Button type="link" disabled={busy} onClick={() => { auth.cancelChallenge(); setError(null); }}>返回密码登录</Button>
      </Form> : <Form key="password" name="admin-login" layout="vertical" requiredMark={false} onFinish={async (values: { userName: string; password: string }) => { setBusy(true); setError(null); try { await auth.login(values.userName, values.password); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}>
        <Form.Item name="userName" label="用户名" rules={[{ required: true, message: '请输入用户名' }]}><Input size="large" prefix={<UserOutlined />} autoComplete="username" placeholder="输入用户名" maxLength={128} /></Form.Item>
        <Form.Item name="password" label="密码" rules={[{ required: true, message: '请输入密码' }]}><Input.Password size="large" prefix={<LockOutlined />} autoComplete="current-password" placeholder="输入密码" maxLength={1024} /></Form.Item>
        <Button type="primary" size="large" htmlType="submit" aria-label="登录" block loading={busy}>登录 <ArrowRightOutlined aria-hidden /></Button>
      </Form>}<p className="login-help">需要开通账号或重置密码？<br /><span>请联系系统管理员</span></p>
    </div><div className="login-form-footer"><SafetyCertificateOutlined /> KeyGate 授权管理平台</div></section>
  </div>;
}

