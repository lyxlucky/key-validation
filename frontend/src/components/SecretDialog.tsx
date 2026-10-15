import { Alert, App, Button, Space } from 'antd';
import { CopyOutlined, DownloadOutlined } from '@ant-design/icons';
import { downloadText } from '../lib/download';
import { LiveModal } from './LiveModal';
import { copyText } from './CopyValue';

export function SecretDialog({ title, value, description, close, fileName }: { title: string; value: string | null; description: string; close: () => void; fileName?: string }) {
  const { message } = App.useApp();
  return <LiveModal className="policy-editor-modal" title={title} open={!!value} closable={false} maskClosable={false} keyboard={false} width={640} footer={<Space wrap>
    <Button disabled={!value} icon={<DownloadOutlined aria-hidden />} onClick={() => { if (value) downloadText(value, fileName ?? 'keygate-secret.txt'); }}>下载保存</Button>
    <Button disabled={!value} icon={<CopyOutlined aria-hidden />} onClick={async () => { if (!value) return; try { await copyText(value); message.success('已复制，请粘贴保存'); } catch { message.error('复制失败，请下载保存'); } }}>复制</Button>
    <Button type="primary" onClick={close}>已保存，关闭</Button>
  </Space>}>
    <Alert type="warning" showIcon title="请妥善保存" description={description} className="query-error" />
    {value && <pre className="operations-secret" data-testid="one-time-secret">{value}</pre>}
  </LiveModal>;
}
