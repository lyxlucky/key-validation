import { useFormSnapshot } from '../../components/useFormSnapshot';
import { Alert, Form, Input, Modal, Select } from 'antd';

export interface CustomerFormValues { applicationId: string; channelId?: string | null; name: string; email?: string; phone?: string; note?: string }
interface Option { value: string; label: string }

export function CustomerEditor({ open, initial, applications, channels, canChooseChannel, busy, error, save, close }: {
  open: boolean; initial: CustomerFormValues | null; applications: Option[]; channels: (Option & { applicationIds: string[] })[];
  canChooseChannel: boolean; busy: boolean; error: string | null;
  save: (values: CustomerFormValues) => Promise<void>; close: () => void;
}) {
  const [form] = Form.useForm<CustomerFormValues>();
  const applicationId = Form.useWatch('applicationId', form);
  useFormSnapshot(form, initial ?? 'new', open, () => initial ?? { applicationId: undefined, channelId: undefined, name: '', email: '', phone: '', note: '' });
  return <Modal title={initial ? '编辑客户' : '新建客户'} open={open} onCancel={() => { if (!busy) close(); }} closable={{ 'aria-label': '关闭', disabled: busy }}
    okText="保存客户" cancelText="取消" cancelButtonProps={{ disabled: busy }} maskClosable={false} confirmLoading={busy} okButtonProps={{ htmlType: 'submit', form: 'customer-editor-form' }} width={620} destroyOnHidden>
    {error && <Alert type="error" showIcon title={error} className="query-error" />}
    <Form id="customer-editor-form" name="customer-editor" form={form} layout="vertical" disabled={busy} onFinish={save}>
      <Form.Item label="客户应用" name="applicationId" rules={[{ required: true, message: '请选择所属应用' }]} extra={initial ? '客户所属应用和渠道在建档后保持固定。' : undefined}>
        <Select disabled={!!initial || busy} showSearch={{ optionFilterProp: 'label' }} options={applications} placeholder="选择应用" onChange={() => form.setFieldValue('channelId', undefined)} />
      </Form.Item>
      {canChooseChannel && <Form.Item label="所属渠道" name="channelId"><Select disabled={!!initial || busy || !applicationId} allowClear showSearch={{ optionFilterProp: 'label' }} options={channels.filter(item => item.applicationIds.includes(applicationId))} placeholder="平台直营" /></Form.Item>}
      <Form.Item label="客户名称" name="name" rules={[{ required: true, whitespace: true, message: '请输入客户名称' }, { max: 120 }]}><Input maxLength={120} /></Form.Item>
      <div className="form-grid">
        <Form.Item label="邮箱" name="email" rules={[{ type: 'email', message: '请输入有效邮箱' }, { max: 254 }]}><Input maxLength={254} autoComplete="off" /></Form.Item>
        <Form.Item label="电话" name="phone" rules={[{ max: 40 }]}><Input maxLength={40} autoComplete="off" /></Form.Item>
      </div>
      <Form.Item label="备注" name="note" rules={[{ max: 500 }]}><Input.TextArea rows={3} maxLength={500} showCount /></Form.Item>
    </Form>
  </Modal>;
}
