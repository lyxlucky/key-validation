import { Alert, Button, Form, Input, Switch } from 'antd';

export interface VersionFormValues { minimumVersion?: string; latestVersion?: string; enforceMinimum: boolean; updateUrl?: string; releaseNotes?: string; sha256?: string }
const versionPattern = /^(0|[1-9]\d{0,3})\.(0|[1-9]\d{0,3})\.(0|[1-9]\d{0,3})$/;

export function VersionPolicyForm({ initial, busy, error, save }: {
  initial: VersionFormValues; busy: boolean; error: string | null; save: (values: VersionFormValues) => Promise<void>;
}) {
  return <Form name="version-policy" layout="vertical" initialValues={initial} disabled={busy} onFinish={save}>
    {error && <Alert type="error" showIcon title={error} className="query-error" />}
    <div className="form-grid">
      <Form.Item label="最低支持版本" name="minimumVersion" dependencies={['enforceMinimum']} rules={[
        { pattern: versionPattern, message: '请输入 major.minor.patch，每段为 0–9999，不含前导零' },
        ({ getFieldValue }) => ({ validator: async (_, value?: string) => { if (getFieldValue('enforceMinimum') && !value) throw new Error('启用强制限制时须填写最低版本'); } }),
      ]}><Input placeholder="例如：2.1.0" maxLength={14} /></Form.Item>
      <Form.Item label="最新版本" name="latestVersion" dependencies={['minimumVersion']} rules={[
        { pattern: versionPattern, message: '请输入 major.minor.patch，每段为 0–9999，不含前导零' },
        ({ getFieldValue }) => ({ validator: async (_, value?: string) => {
          const minimum = getFieldValue('minimumVersion') as string | undefined;
          if (minimum && !value) throw new Error('填写最低版本时须同时填写最新版本');
          if (!value || !minimum || !versionPattern.test(value) || !versionPattern.test(minimum)) return;
          const low = minimum.split('.').map(Number); const high = value.split('.').map(Number);
          for (let index = 0; index < 3; index++) {
            if (high[index] > low[index]) return;
            if (high[index] < low[index]) throw new Error('最新版本不能低于最低支持版本');
          }
        } }),
      ]}><Input placeholder="例如：2.10.0" maxLength={14} /></Form.Item>
    </div>
    <Form.Item label="强制最低版本" name="enforceMinimum" valuePropName="checked" extra="启用后，缺少版本或低于最低版本的客户端在激活和心跳时会被拒绝。"><Switch aria-label="强制最低版本" /></Form.Item>
    <Form.Item label="更新地址" name="updateUrl" rules={[{ type: 'url', message: '请输入完整更新网址' }, { validator: async (_, value?: string) => {
      if (!value) return;
      let url: URL;
      try { url = new URL(value); } catch { throw new Error('请输入完整更新网址'); }
      if (url.protocol !== 'https:' || url.username || url.password) throw new Error('请使用不含账号密码的 HTTPS 下载地址');
    } }]} extra="使用 HTTPS 下载地址。客户端应在安装前校验发布方签名与摘要。"><Input placeholder="https://downloads.example.com/client" maxLength={2048} /></Form.Item>
    <Form.Item label="安装包 SHA-256" name="sha256" rules={[{ pattern: /^[0-9a-fA-F]{64}$/, message: '请输入 64 位十六进制摘要' }]}><Input maxLength={64} placeholder="可选，64 位十六进制摘要" /></Form.Item>
    <Form.Item label="发布说明" name="releaseNotes" rules={[{ max: 4000, message: '发布说明最多 4000 字' }]}><Input.TextArea rows={4} maxLength={4000} showCount /></Form.Item>
    <Alert className="operations-block" type="info" showIcon title="关闭强制限制时，旧客户端继续按原规则使用；较旧版本可收到升级提示。" />
    <Button type="primary" htmlType="submit" loading={busy}>保存版本策略</Button>
  </Form>;
}
