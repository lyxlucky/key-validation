import { useEffect, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Card, Select, Space, Switch, Table, Tag } from 'antd';
import { ReloadOutlined, SettingOutlined } from '@ant-design/icons';
import { api, queryString } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { Notification } from '../../lib/reliability-types';
import { eventNames } from '../../lib/reliability-types';
import { dateText, emptyTable, PageTitle, QueryError } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';
import { useOperationsCatalog } from './useOperationsCatalog';
import { NotificationPolicyDrawer } from './NotificationPolicyDrawer';

export function NotificationsPage() {
  const [page, setPage] = useState(1);
  const [applicationId, setApplicationId] = useState<string>();
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [policyOpen, setPolicyOpen] = useState(false);
  const [reading, setReading] = useState<string | null>(null);
  const { isAdmin } = useAuth();
  const { message } = App.useApp();
  const cache = useQueryClient();
  const catalog = useOperationsCatalog();
  const notifications = useQuery({ queryKey: ['notifications', page, applicationId, unreadOnly], queryFn: () => api<Page<Notification>>('/admin/notifications' + queryString({ page, pageSize: 20, applicationId, unreadOnly: unreadOnly ? 'true' : undefined })), refetchInterval: 60000 });
  useEffect(() => { if (notifications.data) setPage(current => Math.min(current, Math.max(1, Math.ceil(notifications.data.total / 20)))); }, [notifications.data]);
  async function markRead(id: string) {
    setReading(id);
    try { await api(`/admin/notifications/${id}/read`, { method: 'POST' }); await cache.invalidateQueries({ queryKey: ['notifications'] }); }
    catch (caught) { message.error((caught as Error).message); }
    finally { setReading(null); }
  }
  return <><PageTitle title="通知中心" subtitle="查看发送给当前账号的到期提醒、异常与业务通知。" extra={<Space wrap><Button icon={<ReloadOutlined aria-hidden />} loading={notifications.isFetching} onClick={() => notifications.refetch()}>刷新通知</Button>{isAdmin && <Button type="primary" icon={<SettingOutlined aria-hidden />} onClick={() => setPolicyOpen(true)}>通知设置</Button>}</Space>} />
    <QueryError error={catalog.error} retry={catalog.refetch} /><QueryError error={notifications.error} retry={notifications.refetch} />
    <Card className="data-panel"><div className="operations-content operations-filter">
      <div className="filter-field"><label htmlFor="notification-application">筛选通知应用</label><Select id="notification-application" allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="全部可见应用" value={applicationId} options={catalog.data?.applications.map(app => ({ value: app.id, label: app.name }))} onChange={value => { setApplicationId(value); setPage(1); }} /></div>
      <Space><Switch aria-label="仅看未读" checked={unreadOnly} onChange={value => { setUnreadOnly(value); setPage(1); }} /><span>仅看未读</span></Space>
    </div><Table<Notification> rowKey="id" loading={notifications.isLoading} dataSource={notifications.data?.items} locale={{ emptyText: emptyTable }} scroll={{ x: 950 }} pagination={{ current: page, pageSize: 20, total: notifications.data?.total, onChange: setPage, showSizeChanger: false }} columns={[
      { title: '状态', width: 95, render: (_, item) => <Tag color={item.readAt ? 'default' : 'blue'}>{item.readAt ? '已读' : '未读'}</Tag> },
      { title: '应用', dataIndex: 'applicationId', render: value => catalog.data?.applications.find(app => app.id === value)?.name ?? '系统通知' },
      { title: '通知', width: 300, render: (_, item) => <div><strong>{item.title}</strong><p className="notification-message">{item.message}</p></div> },
      { title: '事件', dataIndex: 'eventType', render: value => eventNames[value] ?? value }, { title: '接收时间', dataIndex: 'createdAt', render: dateText },
      { title: '操作', width: 110, fixed: 'right', render: (_, item) => item.readAt ? <span className="muted">已读</span> : <Button type="link" loading={reading === item.id} disabled={!!reading && reading !== item.id} onClick={() => markRead(item.id)}>标为已读</Button> },
    ]} /></Card>
    {isAdmin && <NotificationPolicyDrawer open={policyOpen} close={() => setPolicyOpen(false)} />}
  </>;
}
