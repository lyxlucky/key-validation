import { useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Grid, Select, Table, Tag } from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { api, queryString } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { ApiCredential, ApiCredentialSecret, Channel } from '../../lib/commerce-types';
import { permissionNames } from '../../lib/operations-types';
import { dateText, emptyTable, QueryError } from '../../components/common';
import { SecretDialog } from '../../components/SecretDialog';
import { useOperationsCatalog } from '../operations/useOperationsCatalog';
import { allPages, RowActions } from '../commerce/commerce-shared';
import { ApiCredentialEditor } from './ApiCredentialEditor';
import type { CredentialFormValues } from './ApiCredentialEditor';

export function ApiCredentialsPanel() {
  const { message, modal } = App.useApp();
  const cache = useQueryClient();
  const screens = Grid.useBreakpoint();
  const [page, setPage] = useState(1);
  const [applicationId, setApplicationId] = useState<string>();
  const [editorOpen, setEditorOpen] = useState(false);
  const [editing, setEditing] = useState<ApiCredential | null>(null);
  const [secret, setSecret] = useState<string | null>(null);
  const [pendingSecret, setPendingSecret] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const initial = useMemo(() => editing ? { name: editing.name, applicationId: editing.applicationId, channelId: editing.channelId, permissions: editing.permissions, isEnabled: editing.isEnabled } : null, [editing]);
  const catalog = useOperationsCatalog();
  const channels = useQuery({ queryKey: ['channels', 'all'], queryFn: () => allPages<Channel>('/admin/channels') });
  const credentials = useQuery({ queryKey: ['api-credentials', page, applicationId], queryFn: () => api<Page<ApiCredential>>('/admin/api-credentials' + queryString({ page, pageSize: 20, applicationId })) });
  async function refresh() { await cache.invalidateQueries({ predicate: query => ['api-credentials', 'audit'].includes(String(query.queryKey[0])) }); }
  async function save(values: CredentialFormValues) {
    if (busy) return;
    setBusy(true); setError(null);
    try {
      if (editing) await api(`/admin/api-credentials/${editing.id}`, { method: 'PATCH', body: { name: values.name.trim(), permissions: values.permissions, isEnabled: values.isEnabled } });
      else { const result = await api<ApiCredentialSecret>('/admin/api-credentials', { method: 'POST', body: { applicationId: values.applicationId, channelId: values.channelId || null, name: values.name.trim(), permissions: values.permissions } }); setPendingSecret(result.secret); }
      setEditorOpen(false); setPage(1); await refresh();
    } catch (caught) { setError((caught as Error).message); }
    finally { setBusy(false); }
  }
  function rotate(credential: ApiCredential) {
    modal.confirm({ title: '轮换 API 凭证', content: `为「${credential.name}」生成新凭证，旧值立即失效。请同步更新调用方配置。`, okText: '确认轮换', cancelText: '取消', onOk: async () => {
      try { const result = await api<ApiCredentialSecret>(`/admin/api-credentials/${credential.id}/rotate`, { method: 'POST' }); setSecret(result.secret); await refresh(); }
      catch (caught) { message.error((caught as Error).message); throw caught; }
    } });
  }
  return <><div className="section-toolbar"><p className="muted">为商家服务端分配应用与渠道范围，以及明确的业务权限。</p><Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => { setEditing(null); setError(null); setEditorOpen(true); }}>创建凭证</Button></div>
    <Alert className="operations-block" type="info" showIcon title="业务凭证仅供你的服务端调用" description="软件客户端使用卡密激活后的会话令牌。请将业务凭证保存在商家服务端，每个凭证每分钟最多 60 次请求。" />
    <QueryError error={credentials.error} retry={credentials.refetch} /><QueryError error={catalog.error} retry={catalog.refetch} /><QueryError error={channels.error} retry={channels.refetch} />
    <Card className="data-panel"><div className="operations-content operations-filter"><div className="filter-field"><label htmlFor="credential-app-filter">凭证应用</label><Select id="credential-app-filter" allowClear showSearch={{ optionFilterProp: 'label' }} value={applicationId} placeholder="全部应用" options={catalog.data?.applications.map(item => ({ value: item.id, label: item.name }))} onChange={value => { setApplicationId(value); setPage(1); }} /></div><Button icon={<ReloadOutlined aria-hidden />} onClick={() => credentials.refetch()}>刷新凭证</Button></div>
      <Table<ApiCredential> rowKey="id" loading={credentials.isLoading} dataSource={credentials.data?.items} locale={{ emptyText: emptyTable }} scroll={{ x: 1100 }} pagination={{ current: page, pageSize: 20, total: credentials.data?.total, onChange: setPage, showSizeChanger: false }} columns={[
        { title: '凭证名称', dataIndex: 'name', width: 185, render: value => <strong>{value}</strong> },
        { title: '应用／渠道', width: 185, render: (_, credential) => <div className="commerce-object">{catalog.data?.applications.find(item => item.id === credential.applicationId)?.name || '应用未记录'}<small>{credential.channelId ? channels.data?.find(item => item.id === credential.channelId)?.name || '限定渠道' : '不限渠道'}</small></div> },
        { title: '允许操作', dataIndex: 'permissions', width: 260, render: (values: string[]) => values.length ? values.map(value => <Tag key={value}>{permissionNames[value] ?? value}</Tag>) : <span className="muted">未授予操作权限</span> },
        { title: '状态', dataIndex: 'isEnabled', width: 105, render: value => <Tag color={value ? 'success' : 'default'}>{value ? '已启用' : '已停用'}</Tag> },
        { title: '创建时间', dataIndex: 'createdAt', width: 175, render: dateText },
        { title: '操作', fixed: 'right', width: screens.md ? 200 : 96, render: (_, credential) => <RowActions items={[
          { key: 'edit', label: '编辑', onClick: () => { setEditing(credential); setError(null); setEditorOpen(true); } },
          { key: 'rotate', label: '轮换凭证', onClick: () => rotate(credential) },
        ]} /> },
      ]} />
    </Card>
    <ApiCredentialEditor open={editorOpen} initial={initial} applications={catalog.data?.applications.map(item => ({ value: item.id, label: item.name })) ?? []} channels={channels.data?.map(item => ({ value: item.id, label: item.name, applicationIds: item.applicationIds })) ?? []} busy={busy} error={error} save={save} close={() => setEditorOpen(false)} afterClose={() => { if (pendingSecret) { setSecret(pendingSecret); setPendingSecret(null); } }} />
    <SecretDialog title="保存 API 凭证" value={secret} close={() => setSecret(null)} description="此值只展示一次。服务端请求在 Authorization 头中使用 Bearer 凭证；关闭后不能取回，遗失时请轮换。" />
  </>;
}
