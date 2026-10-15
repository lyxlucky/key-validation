import { planOptions } from '../plans/options';
interface PriceValues { applicationId: string; planId: string; unitPriceCents: number }
import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Drawer, Form, InputNumber, Modal, Select, Table, Tag } from 'antd';
import { api, ApiError } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { Channel, ChannelPrice, ChannelQuota } from '../../lib/commerce-types';
import { dateText, emptyTable, QueryError } from '../../components/common';
import { useOperationsCatalog } from '../operations/useOperationsCatalog';
import { useFormSnapshot } from '../../components/useFormSnapshot';
import { QuotaDialog } from './QuotaDialog';
import { maximumUnitPriceCents, money } from './commerce-shared';

export function ChannelDetail({ id, close }: { id: string | null; close: () => void }) {
  const cache = useQueryClient();
  const { message } = App.useApp();
  const [pagination, setPagination] = useState({ id, page: 1 });
  const page = pagination.id === id ? pagination.page : 1;
  const [quotaId, setQuotaId] = useState<string | null>(null);
  const [pendingQuotas, setPendingQuotas] = useState<Record<string, { requestId: string; delta: number; reason: string }>>({});
  const pendingQuota = id ? pendingQuotas[id] : undefined;
  const [priceOpen, setPriceOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [form] = Form.useForm<PriceValues>();
  const priceApplicationId = Form.useWatch('applicationId', form);
  const detail = useQuery({ queryKey: ['channel', id], queryFn: () => api<Channel>(`/admin/channels/${id}`), enabled: !!id });
  const ledger = useQuery({ queryKey: ['channel-quota', id, page], queryFn: () => api<Page<ChannelQuota>>(`/admin/channels/${id}/quota-ledger?page=${page}&pageSize=10`), enabled: !!id });
  const catalog = useOperationsCatalog(!!id);
  const channel = detail.data;
useFormSnapshot(form, id, priceOpen, () => ({
  applicationId: channel?.applicationIds.length === 1 ? channel.applicationIds[0] : undefined,
  planId: undefined, unitPriceCents: 0,
}));
  async function refresh() { await cache.invalidateQueries({ predicate: query => ['channel', 'channels', 'channel-quota', 'audit'].includes(String(query.queryKey[0])) }); }
  function openQuota() { setError(null); setQuotaId(pendingQuota?.requestId ?? crypto.randomUUID()); }
  async function quota(values: { delta: number; reason: string }) {
    if (!id || !quotaId || busy) return;
    const body = pendingQuota ?? { requestId: quotaId, delta: values.delta, reason: values.reason.trim() };
    setBusy(true); setError(null);
    try {
      await api(`/admin/channels/${id}/quota-adjustments`, { method: 'POST', body });
      setPendingQuotas(current => Object.fromEntries(Object.entries(current).filter(([channelId]) => channelId !== id)));
      setQuotaId(null); await refresh(); message.success('渠道额度已调整');
    } catch (caught) {
      if (pendingQuota || !(caught instanceof ApiError) || caught.status >= 500) setPendingQuotas(current => ({ ...current, [id]: body }));
      setError((caught as Error).message);
    }
    finally { setBusy(false); }
  }
  async function savePrice(values: PriceValues) {
    if (!id || busy) return;
    const plan = catalog.data?.plans.find(item => item.id === values.planId && item.applicationId === values.applicationId); if (!plan || !channel?.applicationIds.includes(values.applicationId) || catalog.isError) return;
    setBusy(true); setError(null);
    try { await api(`/admin/channels/${id}/prices`, { method: 'PUT', body: { applicationId: values.applicationId, planId: plan.id, unitPriceCents: values.unitPriceCents } }); setPriceOpen(false); await refresh(); message.success('渠道售价已保存'); }
    catch (caught) { setError((caught as Error).message); }
    finally { setBusy(false); }
  }
  return <><Drawer title="渠道详情" className="commerce-detail" open={!!id} loading={detail.isLoading} size={800} onClose={() => { if (!busy) { setQuotaId(null); setPriceOpen(false); close(); } }} closable={{ 'aria-label': '关闭', disabled: busy }} destroyOnHidden>
    <QueryError error={detail.error} retry={detail.refetch} /><QueryError error={ledger.error} retry={ledger.refetch} /><QueryError error={catalog.error} retry={catalog.refetch} />
    {channel && <><h3>{channel.name} <Tag color={channel.isEnabled ? 'success' : 'default'}>{channel.isEnabled ? '已启用' : '已停用'}</Tag></h3>
      <div className="commerce-summary"><div><span>剩余发卡额度</span><strong data-testid="channel-remaining-quota">{channel.remainingQuota}</strong></div><div><span>经营应用</span><strong>{channel.applicationIds.length}</strong></div></div>
      <div className="commerce-detail-actions"><Button type="primary" onClick={openQuota}>调整额度</Button><Button onClick={() => { setError(null); setPriceOpen(true); }}>设置售价</Button><Button onClick={refresh}>刷新详情</Button></div>
      {pendingQuota && !quotaId && <Alert className="query-error" type="warning" showIcon title="有一笔额度调整的结果待确认" description="原调整内容已保留，继续确认会使用同一请求，不会重复增加或扣除额度。" action={<Button size="small" onClick={openQuota}>继续确认</Button>} />}
      <section className="commerce-detail-section"><h3>卡种售价</h3><Table<ChannelPrice> rowKey={item => `${item.applicationId}:${item.planId}`} dataSource={channel.prices} pagination={false} locale={{ emptyText: emptyTable }} scroll={{ x: 440 }} columns={[
        { title: '应用', dataIndex: 'applicationId', render: value => catalog.data?.applications.find(item => item.id === value)?.name || '应用未记录' },
        { title: '卡种', dataIndex: 'planId', render: (value, row) => catalog.data?.plans.find(item => item.applicationId === row.applicationId && item.id === value)?.name || '卡种未记录' },
        { title: '售价', dataIndex: 'unitPriceCents', width: 135, render: money },
      ]} /></section>
      <section className="commerce-detail-section"><h3>额度账本</h3><Table<ChannelQuota> rowKey="id" dataSource={ledger.data?.items} loading={ledger.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 600 }} pagination={{ current: page, pageSize: 10, total: ledger.data?.total, onChange: page => setPagination({ id, page }), showSizeChanger: false }} columns={[
        { title: '原因', dataIndex: 'reason' }, { title: '变动', dataIndex: 'delta', width: 95, render: value => <Tag color={value > 0 ? 'success' : 'default'}>{value > 0 ? '+' : ''}{value}</Tag> },
        { title: '变动后余额', dataIndex: 'balanceAfter', width: 110 }, { title: '时间', dataIndex: 'createdAt', width: 180, render: dateText },
      ]} /></section>
    </>}
  </Drawer>
    <QuotaDialog key={`${id}:${quotaId}`} open={!!quotaId} channelName={channel?.name ?? ''} remainingQuota={Number(channel?.remainingQuota ?? 0)} pending={pendingQuota} busy={busy} error={error} save={quota} close={() => setQuotaId(null)} />
    <Modal title="设置渠道售价" open={priceOpen} onCancel={() => { if (!busy) setPriceOpen(false); }} closable={{ 'aria-label': '关闭', disabled: busy }} destroyOnHidden okText="保存售价" cancelText="取消" cancelButtonProps={{ disabled: busy }} maskClosable={false} confirmLoading={busy} okButtonProps={{ htmlType: 'submit', form: 'channel-price-form' }}>
      {error && <Alert type="error" showIcon title={error} className="query-error" />}
      <p className="commerce-description">价格用于之后创建的渠道订单，已有订单保留原快照。</p>
      <Form id="channel-price-form" name="channel-price" layout="vertical" form={form} onFinish={savePrice} disabled={busy}>
<Form.Item label="定价应用" name="applicationId"
  rules={[{ required: true, message: '请选择定价应用' }]}>
  <Select showSearch={{ optionFilterProp: 'label' }}
    options={catalog.data?.applications.filter(app => channel?.applicationIds.includes(app.id))
      .map(app => ({ value: app.id, label: app.name }))}
    onChange={() => form.setFieldsValue({ planId: undefined, unitPriceCents: 0 })} />
</Form.Item>
<Form.Item label="定价卡种" name="planId"
  rules={[{ required: true, message: '请选择卡种' }]}>
  <Select showSearch={{ optionFilterProp: 'label' }} disabled={!priceApplicationId}
    options={planOptions(catalog.data?.plans, priceApplicationId, 'history')}
    onChange={planId => form.setFieldValue('unitPriceCents', Number(channel?.prices.find(
      item => item.applicationId === priceApplicationId && item.planId === planId
    )?.unitPriceCents ?? 0))} />
</Form.Item>
        <Form.Item label="售价（分）" name="unitPriceCents" rules={[{ required: true, type: 'integer', min: 0, max: maximumUnitPriceCents, message: '请输入有效的非负整数金额' }]} extra="100 分 = 1 元人民币。"><InputNumber min={0} max={maximumUnitPriceCents} precision={0} style={{ width: '100%' }} /></Form.Item>
      </Form>
    </Modal>
  </>;
}
