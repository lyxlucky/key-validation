import { planOptions } from '../plans/options';
import { useQuery } from '@tanstack/react-query';
import { Alert, Form, InputNumber, Modal, Radio, Select } from 'antd';
import { api, queryString } from '../../lib/api';
import type { OrderCatalog } from '../../lib/commerce-types';
import type { OperationsCatalog } from '../../lib/operations-types';
import { QueryError } from '../../components/common';
import { useFormSnapshot } from '../../components/useFormSnapshot';
import { maximumUnitPriceCents } from './commerce-shared';

export interface OrderFormValues { applicationId: string; customerId: string; planId: string; type: 'issue' | 'renewal'; quantity: number; unitPriceCents: number; licenseId?: string; additionalHours?: number }
export interface OrderSubmission { requestId: string; applicationId: string; customerId: string; planId: string; type: string; quantity: number; unitPriceCents: number; licenseId?: string; additionalSeconds?: number }
export interface OrderDraft { requestId: string; applicationId?: string; customerId?: string }

export function OrderEditor({ draft, applications, busy, error, pending, save, close }: {
  draft: OrderDraft | null; applications: OperationsCatalog | undefined; busy: boolean; error: string | null; pending: OrderSubmission | null;
  save: (values: OrderFormValues) => Promise<void>; close: () => void;
}) {
  const [form] = Form.useForm<OrderFormValues>();
  const customerId = Form.useWatch('customerId', form);
  const applicationId = Form.useWatch('applicationId', form);
  const kind = Form.useWatch('type', form) ?? 'issue';
  useFormSnapshot(form, pending ?? draft, !!draft, (): Partial<OrderFormValues> => pending ? { ...pending, type: pending.type === 'renewal' ? 'renewal' : 'issue', additionalHours: pending.additionalSeconds ? pending.additionalSeconds / 3600 : 1 } : { applicationId: draft?.applicationId, customerId: draft?.customerId, planId: undefined, type: 'issue', quantity: 1, unitPriceCents: 0, licenseId: undefined, additionalHours: 1 });
  const catalog = useQuery({ queryKey: ['order-catalog', customerId], queryFn: () => api<OrderCatalog>('/admin/orders/catalog' + queryString({ customerId })), enabled: !!draft });
  const selectedCustomer = catalog.data?.customers.find(item => item.id === customerId);
  return <Modal title="新建订单" open={!!draft} onCancel={() => { if (!busy) close(); }} closable={{ 'aria-label': '关闭', disabled: busy }} destroyOnHidden width={640}
    okText={pending ? '查询并重试原订单' : '保存订单'} cancelText="关闭" cancelButtonProps={{ disabled: busy }} maskClosable={false} confirmLoading={busy} onOk={pending ? () => save(form.getFieldsValue()) : undefined} okButtonProps={pending ? {} : { htmlType: 'submit', form: 'order-editor-form' }}>
    <p className="commerce-description">保存为待履约订单，核对后再人工确认发卡或续期。金额仅用于人工结算记录。</p>
    <QueryError error={catalog.error} retry={catalog.refetch} />
    {error && <Alert type={pending ? 'warning' : 'error'} showIcon title={error} className="query-error" />}
    {pending && <Alert type="info" showIcon className="query-error" title="已保留原订单内容" description="当前提交结果尚未确认。再次提交会先查询原请求，必要时使用同一个请求标识重试，避免重复创建。关闭窗口后，再次点击新建订单可继续处理。" />}
    <Form id="order-editor-form" name="order-editor" form={form} layout="vertical" disabled={busy || !!pending} onFinish={save}>
      <Form.Item label="订单应用" name="applicationId" rules={[{ required: true, message: '请选择应用' }]}><Select showSearch={{ optionFilterProp: 'label' }} options={applications?.applications.map(item => ({ value: item.id, label: item.name }))} placeholder="选择应用" onChange={() => form.setFieldsValue({ customerId: undefined, planId: undefined, licenseId: undefined })} /></Form.Item>
      <Form.Item label="订单客户" name="customerId" rules={[{ required: true, message: '请选择客户' }]}><Select disabled={!applicationId || busy || !!pending} showSearch={{ optionFilterProp: 'label' }} options={catalog.data?.customers.filter(item => item.applicationId === applicationId).map(item => ({ value: item.id, label: item.name || '已匿名化客户' }))} placeholder="选择客户" onChange={() => form.setFieldValue('licenseId', undefined)} /></Form.Item>
      <Form.Item label="订单类型" name="type"><Radio.Group options={[{ value: 'issue', label: '发卡' }, { value: 'renewal', label: '续期' }]} onChange={() => form.setFieldsValue({ quantity: 1, licenseId: undefined })} /></Form.Item>
      <Form.Item label="订单卡种" name="planId" rules={[{ required: true, message: '请选择卡种' }]}><Select disabled={!applicationId || busy || !!pending} showSearch={{ optionFilterProp: 'label' }} options={planOptions(applications?.plans, applicationId, pending ? 'history' : 'issue')} placeholder="选择卡种" /></Form.Item>
      {kind === 'renewal' && <><Form.Item label="续期授权" name="licenseId" rules={[{ required: true, message: '请选择该客户的正式授权' }]}><Select disabled={!customerId || busy || !!pending} showSearch={{ optionFilterProp: 'label' }} options={catalog.data?.licenses.filter(item => item.customerId === customerId).map(item => ({ value: item.id, label: item.keyHint }))} placeholder="选择需要续期的卡密" /></Form.Item>
        <Form.Item label="续期时长（小时）" name="additionalHours" rules={[{ required: true, type: 'integer', min: 1, max: 87600, message: '请输入 1–87600 的整数小时' }]}><InputNumber min={1} max={87600} precision={0} style={{ width: '100%' }} /></Form.Item></>}
      <div className="commerce-form-grid"><Form.Item label="订单数量" name="quantity" rules={[{ required: true, type: 'integer', min: 1, max: kind === 'renewal' ? 1 : 100, message: kind === 'renewal' ? '续期订单数量为 1' : '请输入 1–100 的整数数量' }]}><InputNumber disabled={kind === 'renewal' || busy || !!pending} min={1} max={kind === 'renewal' ? 1 : 100} precision={0} style={{ width: '100%' }} /></Form.Item>
        <Form.Item label="单价（分）" name="unitPriceCents" rules={[{ required: true, type: 'integer', min: 0, max: maximumUnitPriceCents, message: '请输入有效的非负整数金额' }]} extra={selectedCustomer?.channelId ? '此客户属于渠道，实际价格以渠道配置为准。保存后可核对订单快照。' : '100 分 = 1 元人民币。'}><InputNumber disabled={!!selectedCustomer?.channelId || busy || !!pending} min={0} max={maximumUnitPriceCents} precision={0} style={{ width: '100%' }} /></Form.Item></div>
    </Form>
  </Modal>;
}
