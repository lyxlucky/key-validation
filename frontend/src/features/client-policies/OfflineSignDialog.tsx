import { Alert, Form, Input, InputNumber, Modal } from 'antd';
import { useFormSnapshot } from '../../components/useFormSnapshot';

export interface OfflineSignValues { licenseId: string; bindingId: string; deviceId: string; hours: number }
const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function OfflineSignDialog({ open, initial, busy, error, save, close }: {
  open: boolean; initial?: Partial<Pick<OfflineSignValues, 'licenseId' | 'bindingId'>>; busy: boolean; error: string | null;
  save: (values: OfflineSignValues) => Promise<void>; close: () => void;
}) {
  const [form] = Form.useForm<OfflineSignValues>();
  useFormSnapshot(form, initial ?? 'new', open, () => ({ licenseId: initial?.licenseId ?? '', bindingId: initial?.bindingId ?? '', hours: 24, deviceId: '' }));
  return <Modal className="policy-editor-modal" title="签发离线授权" open={open} onCancel={() => { if (!busy) close(); }} closable={{ 'aria-label': '关闭', disabled: busy }}
    okText="确认签发" cancelText="取消" cancelButtonProps={{ disabled: busy }} maskClosable={false} confirmLoading={busy} okButtonProps={{ htmlType: 'submit', form: 'offline-sign-form' }} width={640} destroyOnHidden>
    <Alert className="operations-block" type="warning" showIcon title="后台撤销不会立即使离线许可证失效" description="冻结、禁用或释放设备后，已签发的离线权限可能持续至许可证到期。同一绑定重新签发会撤销旧签发记录。" />
    {error && <Alert type="error" showIcon title={error} className="query-error" />}
    <Form id="offline-sign-form" name="offline-sign" form={form} layout="vertical" disabled={busy} onFinish={save}>
      <Form.Item label="授权 ID" name="licenseId" rules={[{ required: true, message: '请输入授权 ID' }, { pattern: guidPattern, message: '请输入完整的授权 ID' }]}><Input placeholder="卡密详情中的授权 ID" autoComplete="off" /></Form.Item>
      <Form.Item label="绑定 ID" name="bindingId" rules={[{ required: true, message: '请输入设备绑定 ID' }, { pattern: guidPattern, message: '请输入完整的绑定 ID' }]}><Input placeholder="待签发设备的绑定 ID" autoComplete="off" /></Form.Item>
      <Form.Item label="原始设备标识" name="deviceId" rules={[{ required: true, whitespace: true, message: '请输入激活时使用的原始设备标识' }, { max: 256 }]} extra="须与该设备激活时提交的标识完全一致。服务器会核对绑定，原文不会存入签发记录。"><Input.Password autoComplete="off" maxLength={256} /></Form.Item>
      <Form.Item label="有效小时" name="hours" rules={[{ required: true, type: 'integer', min: 1, max: 168, message: '请输入 1–168 的整数小时' }]} extra="实际期限不超过卡密当前到期时间。"><InputNumber min={1} max={168} precision={0} style={{ width: '100%' }} /></Form.Item>
    </Form>
  </Modal>;
}
