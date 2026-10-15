import { planOptions } from '../plans/options';
import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Alert, App, Descriptions, Form, Input, InputNumber, Modal, Radio, Select } from 'antd';
import { api } from '../../lib/api';
import type { PortalRequest } from '../../lib/commerce-types';
import type { OperationsCatalog } from '../../lib/operations-types';
import { dateText } from '../../components/common';
import { useFormSnapshot } from '../../components/useFormSnapshot';
import { maximumUnitPriceCents, RequestStatus, requestTypeNames } from './commerce-shared';

interface ReviewValues { status: 'approved' | 'rejected'; reason: string; planId?: string; unitPriceCents?: number; additionalHours?: number }
export function PortalRequestReview({ request, customerName, catalog, close }: { request: PortalRequest | null; customerName: string; catalog: OperationsCatalog | undefined; close: () => void }) {
  const cache = useQueryClient();
  const { message } = App.useApp();
  const [form] = Form.useForm<ReviewValues>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const status = Form.useWatch('status', form);
  useFormSnapshot(form, request, !!request, () => ({ status: undefined, reason: '', planId: undefined, unitPriceCents: 0, additionalHours: 1 }));
  const pending = request?.status === 'pending';
  const renewal = request?.type === 'renewal' && status === 'approved';
  async function save(values: ReviewValues) {
    if (!request || busy) return;
    setBusy(true); setError(null);
    try {
      await api(`/admin/portal-requests/${request.id}/review`, { method: 'POST', body: { status: values.status, reason: values.reason.trim(), ...(renewal ? { planId: values.planId, unitPriceCents: values.unitPriceCents, additionalSeconds: (values.additionalHours ?? 1) * 3600 } : {}) } });
      close(); await cache.invalidateQueries({ predicate: query => ['portal-admin-requests', 'orders', 'order', 'licenses', 'license', 'customer-licenses', 'channels', 'channel', 'channel-quota', 'audit'].includes(String(query.queryKey[0])) }); message.success('申请处理结果已保存');
    } catch (caught) { setError((caught as Error).message); }
    finally { setBusy(false); }
  }
  return <Modal title={pending ? '审批客户申请' : '客户申请结果'} open={!!request} width={640} onCancel={() => { if (!busy) { setError(null); close(); } }} closable={{ 'aria-label': '关闭', disabled: busy }} destroyOnHidden
    okText={pending ? '保存审批' : '关闭'} cancelText="取消" cancelButtonProps={{ disabled: busy }} maskClosable={false} confirmLoading={busy} okButtonProps={pending ? { htmlType: 'submit', form: 'portal-review-form' } : {}} onOk={pending ? undefined : close}>
    {request && <Descriptions column={1} items={[
      { key: 'customer', label: '客户', children: customerName }, { key: 'type', label: '申请类型', children: requestTypeNames[request.type] },
      { key: 'note', label: '申请说明', children: request.note || '未填写' }, { key: 'created', label: '提交时间', children: dateText(request.createdAt) },
      { key: 'status', label: '状态', children: <RequestStatus status={request.status} /> },
      ...(!pending ? [{ key: 'reviewed', label: '处理时间', children: dateText(request.reviewedAt) }, { key: 'reason', label: '审批原因', children: request.reviewReason }] : []),
    ]} />}
    {error && <Alert type="error" showIcon title={error} className="query-error" />}
    {pending && <Form id="portal-review-form" name="portal-review" layout="vertical" form={form} disabled={busy} onFinish={save}>
      <Form.Item label="审批结果" name="status" rules={[{ required: true, message: '请选择审批结果' }]}><Radio.Group options={[{ value: 'approved', label: '批准申请' }, { value: 'rejected', label: '拒绝申请' }]} /></Form.Item>
      {renewal && <><Alert type="info" showIcon className="query-error" title="批准后立即建立并履约续期订单" description="请核对时长与金额。渠道客户采用渠道售价，并消耗 1 个渠道额度。" />
        <Form.Item label="续期卡种" name="planId" rules={[{ required: true, message: '请选择续期卡种' }]}><Select showSearch={{ optionFilterProp: 'label' }} options={planOptions(catalog?.plans, request?.applicationId, 'issue')} /></Form.Item>
        <div className="commerce-form-grid"><Form.Item label="续期时长（小时）" name="additionalHours" rules={[{ required: true, type: 'integer', min: 1, max: 87600, message: '请输入 1–87600 的整数小时' }]}><InputNumber min={1} max={87600} precision={0} style={{ width: '100%' }} /></Form.Item>
          <Form.Item label="单价（分）" name="unitPriceCents" rules={[{ required: true, type: 'integer', min: 0, max: maximumUnitPriceCents, message: '请输入有效的非负整数金额' }]}><InputNumber min={0} max={maximumUnitPriceCents} precision={0} disabled={!!request?.channelId || busy} style={{ width: '100%' }} /></Form.Item></div></>}
      <Form.Item label="审批原因" name="reason" rules={[{ required: true, whitespace: true, message: '请填写审批原因' }, { max: 500 }]}><Input.TextArea rows={3} maxLength={500} showCount /></Form.Item>
    </Form>}
  </Modal>;
}
