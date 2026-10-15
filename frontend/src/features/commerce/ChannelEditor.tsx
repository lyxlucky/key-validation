import { useFormSnapshot } from '../../components/useFormSnapshot';
import { Alert, Form, Input, InputNumber, Modal, Select } from 'antd';

export interface ChannelFormValues { name: string; accountId: string; applicationIds: string[]; initialQuota: number }
interface Option { value: string; label: string }

export function ChannelEditor({ open, initial, applications, accounts, busy, error, save, close }: {
  open: boolean; initial: ChannelFormValues | null; applications: Option[]; accounts: Option[];
  busy: boolean; error: string | null; save: (values: ChannelFormValues) => Promise<void>; close: () => void;
}) {
  const [form] = Form.useForm<ChannelFormValues>();
  useFormSnapshot(form, initial ?? 'new', open, () => initial ?? { name: '', accountId: undefined, applicationIds: [], initialQuota: 0 });
  return <Modal title={initial ? '编辑渠道' : '新建渠道'} open={open} onCancel={() => { if (!busy) close(); }} closable={{ 'aria-label': '关闭', disabled: busy }}
    okText="保存渠道" cancelText="取消" cancelButtonProps={{ disabled: busy }} maskClosable={false} confirmLoading={busy} okButtonProps={{ htmlType: 'submit', form: 'channel-editor-form' }} width={600} destroyOnHidden>
    {error && <Alert type="error" showIcon title={error} className="query-error" />}
    <Form id="channel-editor-form" name="channel-editor" form={form} layout="vertical" disabled={busy} onFinish={save}>
      <Form.Item label="渠道名称" name="name" rules={[{ required: true, whitespace: true, message: '请输入渠道名称' }, { max: 120 }]}><Input maxLength={120} /></Form.Item>
      <Form.Item label="渠道账号" name="accountId" rules={[{ required: true, message: '请选择一个后台账号' }]} extra="绑定一个非管理员账号；该账号的业务范围同时受应用授权和渠道范围限制。"><Select showSearch={{ optionFilterProp: 'label' }} options={accounts} placeholder="选择后台账号" /></Form.Item>
      <Form.Item label="授权应用" name="applicationIds" rules={[{ required: true, type: 'array', min: 1, message: '请至少选择一个应用' }]}><Select mode="multiple" showSearch={{ optionFilterProp: 'label' }} options={applications} placeholder="选择可经营的应用" /></Form.Item>
      {!initial && <Form.Item label="初始额度" name="initialQuota" rules={[{ required: true, type: 'integer', min: 0, max: Number.MAX_SAFE_INTEGER, message: '请输入有效的非负整数' }]} extra="每张正式发卡或一笔续期订单消耗 1 个额度。后续调整会单独记入额度账本。"><InputNumber min={0} max={Number.MAX_SAFE_INTEGER} precision={0} style={{ width: '100%' }} /></Form.Item>}
    </Form>
  </Modal>;
}
