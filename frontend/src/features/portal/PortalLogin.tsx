import { Alert, Button, Card, Form, Input } from 'antd';
import { KeyOutlined, SafetyCertificateOutlined } from '@ant-design/icons';

export function PortalLogin({ busy, error, login }: { busy: boolean; error: string | null; login: (accessCode: string) => Promise<void> }) {
  const [form] = Form.useForm<{ accessCode: string }>();
  return <main className="portal-login">
    <div className="portal-login-content">
      <div className="portal-brand"><SafetyCertificateOutlined aria-hidden /><span>KeyGate</span></div>
      <Card className="portal-login-card">
        <span className="portal-login-icon"><KeyOutlined aria-hidden /></span>
        <h1>客户自助</h1>
        <p className="muted">使用商家提供的访问码查看订单与授权，提交续期或换绑申请。</p>
        {error && <Alert type="error" showIcon title={error} className="query-error" />}
        <Form name="portal-login" form={form} layout="vertical" requiredMark={false} disabled={busy} onFinish={async values => {
          await login(values.accessCode.trim());
        }}>
          <Form.Item label="客户访问码" name="accessCode" rules={[{ required: true, whitespace: true, message: '请输入商家提供的访问码' }]}>
            <Input.Password autoComplete="off" maxLength={512} placeholder="输入客户访问码" />
          </Form.Item>
          <Button block type="primary" htmlType="submit" loading={busy}>进入自助服务</Button>
        </Form>
        <p className="portal-login-help">访问码遗失或已失效，请联系商家重新提供。</p>
      </Card>
      <p className="portal-footnote">KeyGate · 软件授权自助服务</p>
    </div>
  </main>;
}
