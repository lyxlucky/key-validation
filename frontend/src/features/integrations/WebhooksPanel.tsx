import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Alert, App, Button, Card, Dropdown, Grid, Select, Space, Table, Tag } from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { api, queryString } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { WebhookSecret, WebhookSubscription } from '../../lib/reliability-types';
import { eventNames } from '../../lib/reliability-types';
import { emptyTable, QueryError } from '../../components/common';
import { SecretDialog } from '../../components/SecretDialog';
import { useOperationsCatalog } from '../operations/useOperationsCatalog';
import { WebhookEditor } from './WebhookEditor';
import { WebhookDeliveries } from './WebhookDeliveries';

export function WebhooksPanel() {
  const [page, setPage] = useState(1);
  const [applicationId, setApplicationId] = useState<string>();
  const [editing, setEditing] = useState<WebhookSubscription | null>(null);
  const [editorOpen, setEditorOpen] = useState(false);
  const [deliveriesOpen, setDeliveriesOpen] = useState(false);
  const [deliverySubscription, setDeliverySubscription] = useState<WebhookSubscription | null>(null);
  const [secret, setSecret] = useState<string | null>(null);
  const { message, modal } = App.useApp();
  const screens = Grid.useBreakpoint();
  const catalog = useOperationsCatalog();
  const subscriptions = useQuery({ queryKey: ['webhook-subscriptions', page, applicationId], queryFn: () => api<Page<WebhookSubscription>>('/admin/webhook-subscriptions' + queryString({ page, pageSize: 20, applicationId })) });
  function rotate(subscription: WebhookSubscription) {
    modal.confirm({ title: '轮换 Webhook 签名密钥', content: `将为「${subscription.name}」生成新密钥，请同步更新接收方的验签配置。`, okText: '确认轮换', cancelText: '取消', onOk: async () => {
      try { const result = await api<WebhookSecret>(`/admin/webhook-subscriptions/${subscription.id}/rotate-secret`, { method: 'POST' }); setSecret(result.secret); await subscriptions.refetch(); }
      catch (caught) { message.error((caught as Error).message); }
    } });
  }
  function toggle(subscription: WebhookSubscription) {
    modal.confirm({ title: `${subscription.isEnabled ? '停用' : '启用'}订阅`, content: subscription.isEnabled ? `停止「${subscription.name}」的新投递，历史记录保留。` : `开始向「${subscription.name}」投递所选事件。`, okText: subscription.isEnabled ? '确认停用' : '确认启用', cancelText: '取消', onOk: async () => {
      try { await api(`/admin/webhook-subscriptions/${subscription.id}`, { method: 'PATCH', body: { name: subscription.name, url: subscription.url, eventTypes: subscription.eventTypes, isEnabled: !subscription.isEnabled } }); await subscriptions.refetch(); }
      catch (caught) { message.error((caught as Error).message); }
    } });
  }
  return <><div className="section-toolbar"><p className="muted">向已配置的接收地址发送带签名的业务事件。</p><Space wrap><Button onClick={() => { setDeliverySubscription(null); setDeliveriesOpen(true); }}>全部投递</Button><Button icon={<ReloadOutlined aria-hidden />} loading={subscriptions.isFetching} onClick={() => subscriptions.refetch()}>刷新订阅</Button><Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => { setEditing(null); setEditorOpen(true); }}>创建订阅</Button></Space></div>
    <QueryError error={catalog.error} retry={catalog.refetch} /><QueryError error={subscriptions.error} retry={subscriptions.refetch} />
    <Alert className="operations-block" type="info" showIcon title="事件可能重复或乱序到达，接收方应验证签名并按事件标识去重。失败后最多自动尝试 5 次，24 小时后过期。" />
    <Card className="data-panel"><div className="operations-content operations-filter"><div className="filter-field"><label htmlFor="webhook-application">订阅应用</label><Select id="webhook-application" allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="全部应用" value={applicationId} options={catalog.data?.applications.map(app => ({ value: app.id, label: app.name }))} onChange={value => { setApplicationId(value); setPage(1); }} /></div></div>
    <Table<WebhookSubscription> rowKey="id" dataSource={subscriptions.data?.items} loading={subscriptions.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 1200 }} pagination={{ current: page, pageSize: 20, total: subscriptions.data?.total, onChange: setPage, showSizeChanger: false }} columns={[
      { title: '订阅名称', dataIndex: 'name' }, { title: '应用', dataIndex: 'applicationId', render: value => catalog.data?.applications.find(app => app.id === value)?.name ?? value },
      { title: '接收地址', dataIndex: 'url', width: 240, ellipsis: true }, { title: '事件', dataIndex: 'eventTypes', width: 260, render: (values: string[]) => <Space wrap size={4}>{values.map(value => <Tag key={value}>{eventNames[value] ?? value}</Tag>)}</Space> },
      { title: '状态', dataIndex: 'isEnabled', render: value => <Tag color={value ? 'success' : 'default'}>{value ? '已启用' : '已停用'}</Tag> },
      { title: '操作', width: screens.md ? 280 : 96, fixed: 'right', render: (_, subscription) => screens.md ? <Space size={0}><Button type="link" onClick={() => { setEditing(subscription); setEditorOpen(true); }}>编辑</Button><Button type="link" onClick={() => { setDeliverySubscription(subscription); setDeliveriesOpen(true); }}>投递</Button><Button type="link" onClick={() => rotate(subscription)}>轮换密钥</Button><Button type="link" danger={subscription.isEnabled} onClick={() => toggle(subscription)}>{subscription.isEnabled ? '停用' : '启用'}</Button></Space> : <Dropdown trigger={['click']} menu={{ items: [
        { key: 'edit', label: '编辑', onClick: () => { setEditing(subscription); setEditorOpen(true); } },
        { key: 'deliveries', label: '投递记录', onClick: () => { setDeliverySubscription(subscription); setDeliveriesOpen(true); } },
        { key: 'rotate', label: '轮换密钥', onClick: () => rotate(subscription) },
        { key: 'toggle', label: subscription.isEnabled ? '停用' : '启用', danger: subscription.isEnabled, onClick: () => toggle(subscription) },
      ] }}><Button type="link">操作</Button></Dropdown> },
    ]} /></Card>
    <WebhookEditor open={editorOpen} subscription={editing} close={() => setEditorOpen(false)} showSecret={setSecret} />
    <WebhookDeliveries open={deliveriesOpen} subscription={deliverySubscription} applicationId={applicationId} close={() => setDeliveriesOpen(false)} />
    <SecretDialog title="保存 Webhook 签名密钥" value={secret} close={() => setSecret(null)} description="密钥仅在本次创建或轮换时展示。接收方应先将密钥进行 Base64 解码，再验证事件签名。关闭后如需新密钥，请再次轮换。" />
  </>;
}
