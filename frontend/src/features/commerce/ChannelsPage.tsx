import { useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Card, Grid, Input, Table, Tag } from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { api, queryString } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { Account } from '../../lib/management-types';
import type { Channel } from '../../lib/commerce-types';
import { allRoles } from '../../lib/rbac-types';
import { dateText, emptyTable, PageTitle, QueryError } from '../../components/common';
import { useOperationsCatalog } from '../operations/useOperationsCatalog';
import { ChannelEditor } from './ChannelEditor';
import type { ChannelFormValues } from './ChannelEditor';
import { ChannelDetail } from './ChannelDetail';
import { allPages, RowActions } from './commerce-shared';
import './commerce.css';

export function ChannelsPage() {
  const { message, modal } = App.useApp();
  const cache = useQueryClient();
  const screens = Grid.useBreakpoint();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [editorOpen, setEditorOpen] = useState(false);
  const [editing, setEditing] = useState<Channel | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const initial = useMemo(() => editing ? { name: editing.name, accountId: editing.accountId, applicationIds: editing.applicationIds, initialQuota: Number(editing.remainingQuota) } : null, [editing]);
  const channels = useQuery({ queryKey: ['channels', page, search], queryFn: () => api<Page<Channel>>('/admin/channels' + queryString({ page, pageSize: 20, search: search.trim() })) });
  const catalog = useOperationsCatalog();
  const accounts = useQuery({ queryKey: ['accounts', 'all'], queryFn: () => allPages<Account>('/admin/accounts') });
  const roles = useQuery({ queryKey: ['roles', 'all'], queryFn: allRoles, enabled: editorOpen });
  const eligibleAccounts = accounts.data?.filter(account => !account.disabledAt && !account.roleIds.some(id => roles.data?.some(role => role.id === id && role.permissions.includes('platform.manage')))) ?? [];
  async function refresh() { await cache.invalidateQueries({ predicate: query => ['channels', 'channel', 'accounts', 'audit'].includes(String(query.queryKey[0])) }); }
  async function save(values: ChannelFormValues) {
    if (busy) return;
    setBusy(true); setError(null);
    try { await api(editing ? `/admin/channels/${editing.id}` : '/admin/channels', { method: editing ? 'PATCH' : 'POST', body: editing ? { name: values.name.trim(), accountId: values.accountId, applicationIds: values.applicationIds, isEnabled: editing.isEnabled } : values }); setEditorOpen(false); setPage(1); await refresh(); message.success('渠道已保存'); }
    catch (caught) { setError((caught as Error).message); }
    finally { setBusy(false); }
  }
  function toggle(channel: Channel) {
    modal.confirm({ title: `${channel.isEnabled ? '停用' : '启用'}渠道「${channel.name}」`, content: channel.isEnabled ? '停用后停止该渠道的新业务，已有客户端授权继续按原规则验证。' : '启用后允许该渠道按已分配应用和额度继续经营。', okText: channel.isEnabled ? '确认停用渠道' : '确认启用渠道', cancelText: '取消', okButtonProps: { danger: channel.isEnabled }, onOk: async () => {
      try { await api(`/admin/channels/${channel.id}`, { method: 'PATCH', body: { name: channel.name, accountId: channel.accountId, applicationIds: channel.applicationIds, isEnabled: !channel.isEnabled } }); await refresh(); }
      catch (caught) { message.error((caught as Error).message); throw caught; }
    } });
  }
  return <><PageTitle title="渠道管理" subtitle="分配渠道账号、经营应用、发卡额度与卡种售价。" extra={<Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => { setEditing(null); setError(null); setEditorOpen(true); }}>新建渠道</Button>} />
    <QueryError error={channels.error} retry={channels.refetch} /><QueryError error={catalog.error} retry={catalog.refetch} /><QueryError error={accounts.error} retry={accounts.refetch} /><QueryError error={roles.error} retry={roles.refetch} />
    <Card className="data-panel"><div className="panel-heading"><div><h2 className="panel-title">合作渠道 <span className="count-badge">{channels.data?.total ?? '—'}</span></h2><p className="panel-description">渠道订单统一消耗额度，每次调整都保留原因和账本。</p></div><Button icon={<ReloadOutlined aria-hidden />} onClick={() => channels.refetch()}>刷新</Button></div>
      <div className="filter-bar"><div className="filter-field"><label htmlFor="channel-search">搜索渠道</label><Input id="channel-search" allowClear placeholder="输入渠道名称" value={search} maxLength={120} onChange={event => { setSearch(event.target.value); setPage(1); }} /></div></div>
      <Table<Channel> rowKey="id" dataSource={channels.data?.items} loading={channels.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 1040 }} pagination={{ current: page, pageSize: 20, total: channels.data?.total, onChange: setPage, showSizeChanger: false }} columns={[
        { title: '渠道名称', dataIndex: 'name', width: 185, render: name => <strong>{name}</strong> },
        { title: '绑定账号', dataIndex: 'accountId', width: 170, render: id => accounts.data?.find(item => item.id === id)?.userName || '账号未记录' },
        { title: '经营应用', dataIndex: 'applicationIds', width: 210, render: (ids: string[]) => ids.map(id => <Tag key={id}>{catalog.data?.applications.find(item => item.id === id)?.name || '应用未记录'}</Tag>) },
        { title: '剩余额度', dataIndex: 'remainingQuota', width: 100, render: value => <strong>{value}</strong> },
        { title: '状态', dataIndex: 'isEnabled', width: 100, render: value => <Tag color={value ? 'success' : 'default'}>{value ? '已启用' : '已停用'}</Tag> },
        { title: '创建时间', dataIndex: 'createdAt', width: 175, render: dateText },
        { title: '操作', fixed: 'right', width: screens.md ? 220 : 96, render: (_, channel) => <RowActions items={[
          { key: 'detail', label: '详情', onClick: () => setSelected(channel.id) },
          { key: 'edit', label: '编辑', onClick: () => { setEditing(channel); setError(null); setEditorOpen(true); } },
          { key: 'toggle', label: channel.isEnabled ? '停用' : '启用', danger: channel.isEnabled, onClick: () => toggle(channel) },
        ]} /> },
      ]} />
    </Card>
    <ChannelEditor open={editorOpen} initial={initial} applications={catalog.data?.applications.map(item => ({ value: item.id, label: item.name })) ?? []} accounts={roles.isSuccess ? eligibleAccounts.map(item => ({ value: item.id, label: item.userName })) : []} busy={busy} error={error} save={save} close={() => setEditorOpen(false)} />
    <ChannelDetail id={selected} close={() => setSelected(null)} />
  </>;
}
