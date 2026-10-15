import { useFormSnapshot } from '../../components/useFormSnapshot';
import { Alert, Checkbox, Form, Input, Modal, Select, Switch } from 'antd';

export interface CredentialFormValues { name: string; applicationId: string; channelId?: string | null; permissions: string[]; isEnabled: boolean }
interface Option { value: string; label: string }

export function ApiCredentialEditor({ open, initial, applications, channels, busy, error, save, close, afterClose }: {
  open: boolean; initial: CredentialFormValues | null; applications: Option[]; channels: (Option & { applicationIds: string[] })[];
  busy: boolean; error: string | null; save: (values: CredentialFormValues) => Promise<void>; close: () => void;
  afterClose?: () => void;
}) {
  const [form] = Form.useForm<CredentialFormValues>();
  const applicationId = Form.useWatch('applicationId', form);
  useFormSnapshot(form, initial ?? 'new', open, () => initial ?? { name: '', applicationId: undefined, channelId: undefined, permissions: [], isEnabled: true });
  return <Modal title={initial ? '编辑 API 凭证' : '创建 API 凭证'} open={open} onCancel={() => { if (!busy) close(); }} closable={{ 'aria-label': '关闭', disabled: busy }}
    okText="保存凭证" cancelText="取消" cancelButtonProps={{ disabled: busy }} maskClosable={false} confirmLoading={busy} okButtonProps={{ htmlType: 'submit', form: 'api-credential-form' }} width={620} destroyOnHidden afterClose={afterClose}>
    {error && <Alert type="error" showIcon title={error} className="query-error" />}
    <Form id="api-credential-form" name="api-credential" form={form} layout="vertical" disabled={busy} onFinish={save}>
      <Form.Item label="凭证名称" name="name" rules={[{ required: true, whitespace: true, message: '请输入凭证名称' }, { max: 120 }]}><Input maxLength={120} autoComplete="off" /></Form.Item>
      <Form.Item label="凭证应用" name="applicationId" rules={[{ required: true, message: '请选择所属应用' }]}><Select disabled={!!initial || busy} showSearch={{ optionFilterProp: 'label' }} options={applications} placeholder="选择应用" onChange={() => form.setFieldValue('channelId', undefined)} /></Form.Item>
      <Form.Item label="限定渠道" name="channelId" extra="指定渠道后，凭证只可访问该应用下此渠道的数据。"><Select disabled={!!initial || busy || !applicationId} allowClear showSearch={{ optionFilterProp: 'label' }} options={channels.filter(item => item.applicationIds.includes(applicationId))} placeholder="不限定渠道" /></Form.Item>
      <Form.Item label="允许操作" name="permissions" extra="未勾选任何权限时，凭证不能访问业务数据。"><Checkbox.Group className="permission-options" options={[
        { value: 'licenses.read', label: '读取授权' }, { value: 'customers.manage', label: '管理客户' }, { value: 'orders.manage', label: '管理订单' },
      ]} /></Form.Item>
      {initial && <Form.Item label="启用凭证" name="isEnabled" valuePropName="checked"><Switch /></Form.Item>}
    </Form>
  </Modal>;
}
