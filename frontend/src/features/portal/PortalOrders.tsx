import { useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Alert, App, Button, Card, Descriptions, Drawer, Space, Table, Tag } from 'antd';
import type { Order, OrderDelivery } from '../../lib/commerce-types';
import type { Page } from '../../lib/types';
import { dateText, durationText, emptyTable, QueryError } from '../../components/common';
import { DeliveryDialog } from '../commerce/DeliveryDialog';
import type { DeliveryView } from '../commerce/DeliveryDialog';
import { deliveryView, money, OrderStatus, orderTypeNames } from '../commerce/commerce-shared';
import { usePortal } from './PortalProvider';

export function PortalOrders() {
  const { request } = usePortal();
  const { message } = App.useApp();
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string | null>(null);
  const [delivery, setDelivery] = useState<DeliveryView | null>(null);
  const [busy, setBusy] = useState(false);
  const current = useRef(selected); current.current = selected;
  const orders = useQuery({ queryKey: ['portal-orders', page], queryFn: () => request<Page<Order>>(`/orders?page=${page}&pageSize=20`) });
  const detail = useQuery({ queryKey: ['portal-order', selected], queryFn: () => request<Order>(`/orders/${selected}`), enabled: !!selected });
  const value = detail.data;
  async function showDelivery() {
    if (!value || busy) return;
    setBusy(true);
    try { const result = await request<OrderDelivery>(`/orders/${value.id}/delivery`); if (current.current === value.id) setDelivery(deliveryView(result)); }
    catch (caught) { message.error((caught as Error).message); await detail.refetch(); }
    finally { setBusy(false); }
  }
  return <><QueryError error={orders.error} retry={orders.refetch} /><Card className="data-panel"><div className="panel-heading"><div><h2 className="panel-title">我的订单</h2><p className="panel-description">查看商家确认的订单和交付内容。</p></div><Button onClick={() => orders.refetch()}>刷新订单</Button></div>
    <Table<Order> rowKey="id" dataSource={orders.data?.items} loading={orders.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 700 }} pagination={{ current: page, pageSize: 20, total: orders.data?.total, onChange: setPage, showSizeChanger: false }} columns={[
      { title: '订单内容', width: 220, render: (_, order) => <div className="commerce-object"><strong>{order.snapshot.planName}</strong><small>{orderTypeNames[order.type]} · {order.quantity} 份</small></div> },
      { title: '金额', dataIndex: 'totalCents', width: 120, render: money }, { title: '状态', dataIndex: 'status', width: 105, render: status => <OrderStatus status={status} /> },
      { title: '创建时间', dataIndex: 'createdAt', width: 180, render: dateText }, { title: '操作', fixed: 'right', width: 90, render: (_, order) => <Button type="link" onClick={() => setSelected(order.id)}>详情</Button> },
    ]} />
  </Card><Drawer title="我的订单详情" open={!!selected} loading={detail.isLoading} size={680} onClose={() => { if (!busy) { setSelected(null); setDelivery(null); } }} closable={{ 'aria-label': '关闭', disabled: busy }} destroyOnHidden>
    <QueryError error={detail.error} retry={detail.refetch} />
    {value && <><Space wrap size={12}><OrderStatus status={value.status} /><strong>{value.snapshot.planName}</strong></Space>
      <div className="commerce-summary"><div><span>订单金额</span><strong>{money(value.totalCents)}</strong></div><div><span>数量</span><strong>{value.quantity}</strong></div></div>
      <Descriptions column={{ xs: 1, sm: 2 }} items={[
        { key: 'kind', label: '订单类型', children: orderTypeNames[value.type] }, { key: 'duration', label: '授权时长', children: durationText(value.snapshot.durationSeconds) },
        { key: 'devices', label: '设备上限', children: value.snapshot.maxDevices }, { key: 'online', label: '在线上限', children: value.snapshot.maxConcurrentSessions },
        { key: 'created', label: '创建时间', children: dateText(value.createdAt) }, { key: 'fulfilled', label: '履约时间', children: dateText(value.fulfilledAt) },
        { key: 'features', label: '功能', span: 2, children: value.snapshot.features.length ? value.snapshot.features.map(item => <Tag key={item}>{item}</Tag>) : '无附加功能' },
      ]} />
      {value.deliveryAvailable ? <div className="commerce-detail-actions"><Button type="primary" loading={busy} onClick={showDelivery}>领取交付</Button><span className="muted">保留至 {dateText(value.deliveryExpiresAt)}</span></div> : value.status === 'fulfilled' && value.type === 'issue' && <Alert type="warning" showIcon title="此订单的交付内容已不可取回，请联系商家。" />}
      {value.status === 'pending' && <Alert type="info" showIcon title="等待商家处理" description="商家确认履约后，可以在这里领取交付内容。" />}
    </>}
  </Drawer><DeliveryDialog value={delivery} close={() => setDelivery(null)} /></>;
}
