import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Select, Table } from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { useSearchParams } from 'react-router-dom';
import { api, ApiError, queryString } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { Channel, Order, OrderCatalog } from '../../lib/commerce-types';
import { dateText, emptyTable, PageTitle, QueryError } from '../../components/common';
import { useOperationsCatalog } from '../operations/useOperationsCatalog';
import { useAuth } from '../auth/AuthProvider';
import { OrderEditor } from './OrderEditor';
import type { OrderDraft, OrderFormValues, OrderSubmission } from './OrderEditor';
import { OrderDetail } from './OrderDetail';
import { allPages, money, OrderStatus, orderStatusNames, orderTypeNames } from './commerce-shared';
import './commerce.css';

export function OrdersPage() {
  const [params] = useSearchParams();
  const linkedCustomer = params.get('customerId') ?? undefined;
  const [filters, setFilters] = useState<{ page: number; applicationId?: string; customerId?: string; channelId?: string; status?: string; linkedCustomer?: string }>({ page: 1, customerId: linkedCustomer, linkedCustomer });
  if (filters.linkedCustomer !== linkedCustomer) setFilters(current => ({ ...current, customerId: linkedCustomer, linkedCustomer, page: 1 }));
  const [draft, setDraft] = useState<OrderDraft | null>(null);
  const [editorOpen, setEditorOpen] = useState(false);
  const [pending, setPending] = useState<OrderSubmission | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const cache = useQueryClient();
  const auth = useAuth();
  const { message } = App.useApp();
  const catalog = useOperationsCatalog();
  const channels = useQuery({ queryKey: ['channels', 'all'], queryFn: () => allPages<Channel>('/admin/channels'), enabled: auth.isAdmin });
  const customers = useQuery({ queryKey: ['order-catalog', undefined], queryFn: () => api<OrderCatalog>('/admin/orders/catalog') });
  const orders = useQuery({ queryKey: ['orders', filters], queryFn: () => api<Page<Order>>('/admin/orders' + queryString({ page: filters.page, pageSize: 20, applicationId: filters.applicationId, customerId: filters.customerId, channelId: filters.channelId, status: filters.status })) });
  function open() {
    if (!pending) {
      const customer = customers.data?.customers.find(item => item.id === filters.customerId);
      setDraft({ requestId: crypto.randomUUID(), applicationId: customer?.applicationId ?? filters.applicationId, customerId: customer?.id }); setError(null);
    }
    setEditorOpen(true);
  }
  async function save(values: OrderFormValues) {
    if (!draft || busy) return;
    const body: OrderSubmission = pending ?? { requestId: draft.requestId, applicationId: values.applicationId, customerId: values.customerId, planId: values.planId, type: values.type, quantity: values.type === 'renewal' ? 1 : values.quantity, unitPriceCents: values.unitPriceCents, ...(values.type === 'renewal' ? { licenseId: values.licenseId, additionalSeconds: (values.additionalHours ?? 1) * 3600 } : {}) };
    setBusy(true); setError(null);
    try {
      const recovered = pending ? await api<Page<Order>>('/admin/orders' + queryString({ requestId: body.requestId, pageSize: 1 })) : null;
      const result = recovered?.items[0] ?? await api<Order>('/admin/orders', { method: 'POST', body });
      cache.setQueryData(['order', result.id], result);
      setPending(null); setDraft(null); setEditorOpen(false); message.success('订单已保存，请核对后履约');
      setFilters(current => ({ ...current, page: 1 }));
      await cache.invalidateQueries({ predicate: query => ['orders', 'audit'].includes(String(query.queryKey[0])) });
    } catch (caught) {
      // Once a response is ambiguous, retries must keep the original body even if a
      // later request is rejected. A definitive first rejection can be corrected.
      if (pending || !(caught instanceof ApiError) || caught.status >= 500) setPending(body);
      setError((caught as Error).message);
    } finally { setBusy(false); }
  }
  return <><PageTitle title="订单管理" subtitle="保存客户订单与授权快照，核对后人工履约发卡或续期。" extra={<Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={open}>新建订单</Button>} />
    {pending && !editorOpen && <Alert className="query-error" type="warning" showIcon title="有一笔提交结果待确认的订单" action={<Button size="small" onClick={() => setEditorOpen(true)}>继续处理</Button>} />}
    <QueryError error={orders.error} retry={orders.refetch} /><QueryError error={catalog.error} retry={catalog.refetch} /><QueryError error={customers.error} retry={customers.refetch} /><QueryError error={channels.error} retry={channels.refetch} />
    <Card className="data-panel"><div className="panel-heading"><div><h2 className="panel-title">人工订单 <span className="count-badge">{orders.data?.total ?? '—'}</span></h2><p className="panel-description">履约时按订单快照交付，同一订单重复确认不会重复发卡或扣额度。</p></div><Button icon={<ReloadOutlined aria-hidden />} onClick={() => orders.refetch()}>刷新</Button></div>
      <div className="filter-bar operations-filter">
        <div className="filter-field"><label htmlFor="orders-app">订单应用</label><Select id="orders-app" allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="全部应用" value={filters.applicationId} options={catalog.data?.applications.map(item => ({ value: item.id, label: item.name }))} onChange={applicationId => setFilters(current => ({ ...current, applicationId, customerId: undefined, page: 1 }))} /></div>
        <div className="filter-field"><label htmlFor="orders-customer">客户筛选</label><Select id="orders-customer" allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="全部客户" value={filters.customerId} options={customers.data?.customers.filter(item => !filters.applicationId || item.applicationId === filters.applicationId).map(item => ({ value: item.id, label: item.name || '已匿名化客户' }))} onChange={customerId => setFilters(current => ({ ...current, customerId, page: 1 }))} /></div>
        <div className="filter-field"><label htmlFor="orders-status">订单状态</label><Select id="orders-status" allowClear placeholder="全部状态" value={filters.status} options={Object.entries(orderStatusNames).map(([value, label]) => ({ value, label }))} onChange={status => setFilters(current => ({ ...current, status, page: 1 }))} /></div>
        {auth.isAdmin && <div className="filter-field"><label htmlFor="orders-channel">渠道筛选</label><Select id="orders-channel" allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="全部渠道" value={filters.channelId} options={channels.data?.map(item => ({ value: item.id, label: item.name }))} onChange={channelId => setFilters(current => ({ ...current, channelId, page: 1 }))} /></div>}
      </div>
      <Table<Order> rowKey="id" loading={orders.isLoading} dataSource={orders.data?.items} locale={{ emptyText: emptyTable }} scroll={{ x: 1100 }} pagination={{ current: filters.page, pageSize: 20, total: orders.data?.total, onChange: page => setFilters(current => ({ ...current, page })), showSizeChanger: false, showTotal: total => `共 ${total} 笔订单` }} columns={[
        { title: '客户', dataIndex: 'customerId', width: 180, render: id => <strong>{customers.data?.customers.find(item => item.id === id)?.name || '客户名称未记录'}</strong> },
        { title: '订单内容', width: 230, render: (_, order) => <div className="commerce-object">{order.snapshot.planName}<small>{orderTypeNames[order.type] ?? order.type} · {order.quantity} 份</small></div> },
        { title: '应用', dataIndex: 'applicationId', width: 170, render: id => catalog.data?.applications.find(item => item.id === id)?.name || '应用未记录' },
        { title: '订单金额', dataIndex: 'totalCents', width: 125, render: money }, { title: '状态', dataIndex: 'status', width: 110, render: status => <OrderStatus status={status} /> },
        { title: '创建时间', dataIndex: 'createdAt', width: 175, render: dateText }, { title: '操作', fixed: 'right', width: 90, render: (_, order) => <Button type="link" onClick={() => setSelected(order.id)}>详情</Button> },
      ]} />
    </Card>
    <OrderEditor draft={editorOpen ? draft : null} applications={catalog.data} busy={busy} error={error} pending={pending} save={save} close={() => setEditorOpen(false)} />
    <OrderDetail id={selected} customerName={customers.data?.customers.find(item => item.id === orders.data?.items.find(order => order.id === selected)?.customerId)?.name} close={() => setSelected(null)} />
  </>;
}
