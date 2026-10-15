import { useEffect, useState } from 'react';
import { useRef } from 'react';
import { useQuery } from '@tanstack/react-query';
import { App, Button, Drawer, Select, Space, Table, Tag } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { api, queryString } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { WebhookDelivery, WebhookSubscription } from '../../lib/reliability-types';
import { deliveryStatusNames, eventNames } from '../../lib/reliability-types';
import { dateText, emptyTable, QueryError } from '../../components/common';

export function WebhookDeliveries({ open, subscription, applicationId, close }: { open: boolean; subscription: WebhookSubscription | null; applicationId?: string; close: () => void }) {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<string>();
  const [retrying, setRetrying] = useState<string | null>(null);
  const { message } = App.useApp();
  const deliveries = useQuery({ queryKey: ['webhook-deliveries', page, applicationId, subscription?.id, status], queryFn: () => api<Page<WebhookDelivery>>('/admin/webhook-deliveries' + queryString({ page, pageSize: 20, applicationId, subscriptionId: subscription?.id, status })), enabled: open, refetchInterval: open ? 15000 : false });
  const parameters = JSON.stringify([open, subscription?.id, applicationId]);
  const previousParameters = useRef(parameters);
  useEffect(() => { if (previousParameters.current !== parameters) { previousParameters.current = parameters; setPage(1); setStatus(undefined); } }, [parameters]);
  async function retry(id: string) {
    setRetrying(id);
    try { await api(`/admin/webhook-deliveries/${id}/retry`, { method: 'POST' }); await deliveries.refetch(); message.success('已重新排队，事件标识保持不变'); }
    catch (caught) { message.error((caught as Error).message); }
    finally { setRetrying(null); }
  }
  return <Drawer title={subscription ? `${subscription.name} · 投递记录` : 'Webhook 投递记录'} open={open} onClose={close} size={1000} closable={{ 'aria-label': '关闭' }} destroyOnHidden>
    <QueryError error={deliveries.error} retry={deliveries.refetch} />
    <div className="section-toolbar"><Space wrap><span>投递状态</span><Select aria-label="投递状态" allowClear placeholder="全部状态" value={status} style={{ width: 180 }} options={Object.entries(deliveryStatusNames).map(([value, label]) => ({ value, label }))} onChange={value => { setStatus(value); setPage(1); }} /></Space><Button icon={<ReloadOutlined aria-hidden />} loading={deliveries.isFetching} onClick={() => deliveries.refetch()}>刷新投递</Button></div>
    <Table<WebhookDelivery> rowKey="id" dataSource={deliveries.data?.items} loading={deliveries.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 1200 }} pagination={{ current: page, pageSize: 20, total: deliveries.data?.total, onChange: setPage, showSizeChanger: false }} columns={[
      { title: '事件标识', dataIndex: 'eventId', render: value => <code>{value}</code> }, { title: '事件', dataIndex: 'eventType', render: value => eventNames[value] ?? value },
      { title: '状态', dataIndex: 'status', render: value => <Tag color={value === 'succeeded' ? 'success' : ['failed', 'expired'].includes(value) ? 'error' : 'processing'}>{deliveryStatusNames[value] ?? value}</Tag> },
      { title: '尝试次数', dataIndex: 'attempts' }, { title: '下次尝试', dataIndex: 'nextAttemptAt', render: dateText }, { title: '完成时间', dataIndex: 'completedAt', render: dateText }, { title: '最后错误', dataIndex: 'lastError', render: value => value ?? '—' },
      { title: '操作', width: 105, fixed: 'right', render: (_, delivery) => <Button type="link" disabled={!['failed', 'expired'].includes(delivery.status) || !!retrying} loading={retrying === delivery.id} onClick={() => retry(delivery.id)}>重试投递</Button> },
    ]} />
  </Drawer>;
}
