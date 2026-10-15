import { Alert, App, Button, Modal, Space } from 'antd';
import { CopyOutlined, DownloadOutlined } from '@ant-design/icons';
import { dateText } from '../../components/common';
import { downloadText } from '../../lib/download';
import { copyText } from '../../components/CopyValue';

export interface DeliveryView {
  orderId: string;
  expiresAt: string;
  licenses: { licenseId: string; keyHint: string; key: string }[];
}

export function DeliveryDialog({ value, close }: { value: DeliveryView | null; close: () => void }) {
  const { message } = App.useApp();
  if (!value) return null;
  const text = value.licenses.map(license => license.key).join('\n');
  return <Modal title="订单交付" open width={700} onCancel={close} destroyOnHidden closable={{ 'aria-label': '关闭' }} footer={<Space wrap>
    <Button icon={<DownloadOutlined aria-hidden />} onClick={() => downloadText(text, `order-${value.orderId}.txt`)}>下载卡密</Button>
    <Button icon={<CopyOutlined aria-hidden />} onClick={async () => { try { await copyText(text); message.success('已复制交付卡密'); } catch { message.error('复制失败，请手动保存或下载'); } }}>复制全部</Button>
    <Button type="primary" onClick={close}>关闭交付</Button>
  </Space>}>
    <Alert type="warning" showIcon className="operations-block" title="请妥善保存交付卡密" description={`此订单的交付内容保留至 ${dateText(value.expiresAt)}。到期后无法再次取回，请通过已核实的渠道提供给客户。`} />
    {value.licenses.map(license => <div key={license.licenseId} className="delivery-item"><span className="muted">卡密提示：{license.keyHint}</span><pre className="operations-secret" data-testid="order-delivery-key" data-license-id={license.licenseId}>{license.key}</pre></div>)}
  </Modal>;
}
