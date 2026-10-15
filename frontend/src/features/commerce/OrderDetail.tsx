import { useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Collapse, Descriptions, Drawer, Space, Tag, Timeline } from 'antd';
import { api } from '../../lib/api';
import type { Order, OrderDelivery } from '../../lib/commerce-types';
import { dateText, durationText, QueryError } from '../../components/common';
import { DeliveryDialog } from './DeliveryDialog';
import type { DeliveryView } from './DeliveryDialog';
import { CopyValue } from '../../components/CopyValue';
import { deliveryView, money, OrderStatus, orderTypeNames } from './commerce-shared';

export function OrderDetail({ id, customerName, close }: { id: string | null; customerName?: string | null; close: () => void }) {
  const { modal, message } = App.useApp();
  const cache = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [delivery, setDelivery] = useState<DeliveryView | null>(null);
  const requestIds = useRef(new Map<string, string>());
  const currentId = useRef(id); currentId.current = id;
  const detail = useQuery({ queryKey: ['order', id], queryFn: () => api<Order>(`/admin/orders/${id}`), enabled: !!id });
  const order = detail.data;
  async function refresh() { await cache.invalidateQueries({ predicate: query => ['orders', 'order', 'customer-licenses', 'licenses', 'license', 'batches', 'dashboard', 'operations-report', 'channels', 'channel', 'channel-quota', 'audit'].includes(String(query.queryKey[0])) }); }
  function fulfill() {
    if (!order) return;
    const key = requestIds.current.get(order.id) ?? crypto.randomUUID(); requestIds.current.set(order.id, key);
    modal.confirm({ title: '确认履约此订单', content: order.type === 'issue' ? `按订单快照生成 ${order.quantity} 张「${order.snapshot.planName}」。${order.channelId ? '将扣除对应渠道额度。' : ''}订单金额 ${money(order.totalCents)}。` : `按订单快照为客户的授权续期 ${durationText(order.snapshot.durationSeconds)}。${order.channelId ? '将扣除 1 个渠道额度。' : ''}`, okText: '完成履约', cancelText: '取消', onOk: async () => {
      setBusy(true);
      try { await api<Order>(`/admin/orders/${order.id}/fulfill`, { method: 'POST', body: { requestId: key } }); await refresh(); message.success('订单已履约'); }
      catch (caught) { message.error((caught as Error).message); throw caught; }
      finally { setBusy(false); }
    } });
  }
  function cancel() {
    if (!order) return;
    modal.confirm({ title: '取消待履约订单', content: '取消后不再发卡或续期，不扣除渠道额度。已履约订单不能取消。', okText: '确认取消订单', cancelText: '返回', okButtonProps: { danger: true }, onOk: async () => {
      setBusy(true);
      try { await api(`/admin/orders/${order.id}/cancel`, { method: 'POST' }); await refresh(); }
      catch (caught) { message.error((caught as Error).message); throw caught; }
      finally { setBusy(false); }
    } });
  }
  async function showDelivery() {
    if (!order || busy) return;
    setBusy(true);
    try { const value = await api<OrderDelivery>(`/admin/orders/${order.id}/delivery`); if (currentId.current === order.id) setDelivery(deliveryView(value)); }
    catch (caught) { message.error((caught as Error).message); await detail.refetch(); }
    finally { setBusy(false); }
  }
  return <><Drawer title="订单详情" className="commerce-detail" open={!!id} loading={detail.isLoading} size={780} onClose={() => { if (!busy) { setDelivery(null); close(); } }} closable={{ 'aria-label': '关闭', disabled: busy }} destroyOnHidden>
    <QueryError error={detail.error} retry={detail.refetch} />
    {order && <><Space size={12} wrap><OrderStatus status={order.status} /><strong>{order.snapshot.planName}</strong><span>{orderTypeNames[order.type] ?? order.type}</span></Space>
      <div className="commerce-summary"><div><span>订单金额</span><strong>{money(order.totalCents)}</strong></div><div><span>数量</span><strong>{order.quantity}</strong></div><div><span>单价</span><strong>{money(order.unitPriceCents)}</strong></div></div>
      <Descriptions column={{ xs: 1, sm: 2 }} items={[
        { key: 'customer', label: '客户', children: customerName || '见客户档案' }, { key: 'channel', label: '订单来源', children: order.channelId ? '渠道订单' : '平台直营' },
        { key: 'created', label: '创建时间', children: dateText(order.createdAt) }, { key: 'fulfilled', label: '履约时间', children: dateText(order.fulfilledAt) },
        { key: 'duration', label: order.type === 'renewal' ? '续期时长' : '授权时长', children: durationText(order.snapshot.durationSeconds) },
        { key: 'devices', label: '设备／在线上限', children: `${order.snapshot.maxDevices} 台设备／${order.snapshot.maxConcurrentSessions} 个会话` },
        { key: 'activation', label: '最晚激活期限', children: order.snapshot.activationValidityDays ? `发卡后 ${order.snapshot.activationValidityDays} 天内` : '不限' },
        { key: 'features', label: '功能快照', children: order.snapshot.features.length ? <Space size={4} wrap>{order.snapshot.features.map(item => <Tag key={item}>{item}</Tag>)}</Space> : '无附加功能' },
      ]} />
      <div className="commerce-detail-actions">{order.status === 'pending' && <><Button type="primary" loading={busy} onClick={fulfill}>确认履约</Button><Button danger disabled={busy} onClick={cancel}>取消订单</Button></>}{order.deliveryAvailable && <Button type="primary" loading={busy} onClick={showDelivery}>查看交付</Button>}<Button disabled={busy} onClick={() => detail.refetch()}>刷新详情</Button></div>
      {order.status === 'fulfilled' && order.type === 'issue' && <Alert type={order.deliveryAvailable ? 'info' : 'warning'} showIcon title={order.deliveryAvailable ? `交付内容保留至 ${dateText(order.deliveryExpiresAt)}` : '交付内容已不可取回'} description={order.deliveryAvailable ? '请在保留期限内通过专用交付窗口下载，或由客户在自助服务中领取。' : '授权和订单历史仍然保留。已经过期或清除的卡密明文无法恢复。'} />}
      <section className="commerce-detail-section"><h3>处理历史</h3><Timeline items={order.history.map(item => ({ title: dateText(item.createdAt), content: ({ created: '创建订单', fulfilled: '完成履约', cancelled: '取消订单', 'order.create': '创建订单', 'order.fulfilled': '完成履约', 'order.cancel': '取消订单' } as Record<string, string>)[item.action] ?? item.action }))} /></section>
      <Collapse items={[{ key: 'references', label: '订单与授权关联标识', children: <Descriptions column={1} items={[
        { key: 'id', label: '订单 ID', children: <CopyValue value={order.id} label="订单 ID" /> },
        { key: 'request', label: '请求 ID', children: <CopyValue value={order.requestId} label="请求 ID" /> },
        { key: 'batch', label: '发行批次', children: order.batchId ? <CopyValue value={order.batchId} label="批次 ID" /> : '无' },
        { key: 'licenses', label: '关联授权', children: order.licenseIds.length ? order.licenseIds.map(licenseId => <div key={licenseId} className="commerce-reference">{licenseId}</div>) : '尚未生成' },
      ]} /> }]} />
    </>}
  </Drawer><DeliveryDialog value={id ? delivery : null} close={() => setDelivery(null)} /></>;
}
