import { Alert, Form, Input, InputNumber, Modal } from 'antd';

export function QuotaDialog({ open, channelName, remainingQuota, pending, busy, error, save, close }: {
  open: boolean; channelName: string; remainingQuota: number; busy: boolean; error: string | null;
  pending?: { delta: number; reason: string };
  save: (values: { delta: number; reason: string }) => Promise<void>; close: () => void;
}) {
  return <Modal title="调整渠道额度" open={open} onCancel={() => { if (!busy) close(); }} closable={{ 'aria-label': '关闭', disabled: busy }}
    okText={pending ? '重试原调整' : '确认调整'} cancelText={pending ? '关闭' : '取消'} cancelButtonProps={{ disabled: busy }} maskClosable={false} confirmLoading={busy} onOk={pending ? () => save(pending) : undefined} okButtonProps={pending ? {} : { htmlType: 'submit', form: 'channel-quota-form' }} destroyOnHidden>
    <p>「{channelName}」当前剩余额度：<strong>{remainingQuota}</strong>。每张正式发卡或一笔续期订单消耗 1 个额度。</p>
    {error && <Alert type="error" showIcon title={error} className="query-error" />}
    {pending && <Alert type="warning" showIcon title="已保留原调整内容" description="提交结果尚未确认。重试时使用原请求标识和内容，避免重复调整。" className="query-error" />}
    <Form id="channel-quota-form" name="channel-quota" layout="vertical" disabled={busy || !!pending} onFinish={save} initialValues={pending ?? { delta: 0, reason: '' }}>
      <Form.Item label="调整数量" name="delta" rules={[{ required: true, type: 'integer', min: -Number.MAX_SAFE_INTEGER, max: Number.MAX_SAFE_INTEGER, message: '请输入有效的整数' }, { validator: async (_, value: number) => { if (value === 0) throw new Error('调整数量不能为 0'); } }]} extra="正数增加，负数扣减；扣减后不能低于 0。"><InputNumber min={-Number.MAX_SAFE_INTEGER} max={Number.MAX_SAFE_INTEGER} precision={0} style={{ width: '100%' }} /></Form.Item>
      <Form.Item label="调整原因" name="reason" rules={[{ required: true, whitespace: true, message: '请填写调整原因' }, { max: 500 }]}><Input.TextArea maxLength={500} rows={3} /></Form.Item>
    </Form>
  </Modal>;
}
