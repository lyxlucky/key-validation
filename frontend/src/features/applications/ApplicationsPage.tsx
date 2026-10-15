import { invalidatePlanQueries } from '../plans/queries';
﻿import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Empty, Form, Grid, Input, Modal, Space, Table, Tag } from 'antd';
import { AppstoreOutlined, CheckCircleOutlined, PlusOutlined, ReloadOutlined, StopOutlined } from '@ant-design/icons';
import { allApplications, api } from '../../lib/api';
import type { Application, Page } from '../../lib/types';
import { dateText, PageTitle, QueryError } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';
import { ApplicationSettingsDrawer } from './ApplicationSettingsDrawer';
import { CopyValue } from '../../components/CopyValue';

export function ApplicationsPage() {
  const [page, setPage] = useState(1);
  const [createOpen, setCreateOpen] = useState(false);
  const [selected, setSelected] = useState<Application | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [created, setCreated] = useState<Application | null>(null);
  const [search, setSearch] = useState('');
  const [form] = Form.useForm();
  const screens = Grid.useBreakpoint();
  const { modal, message } = App.useApp();
  const { isAdmin } = useAuth();
  const cache = useQueryClient();
  const apps = useQuery({ queryKey: ['applications', page], queryFn: () => api<Page<Application>>(`/admin/applications?page=${page}&pageSize=20`) });
  const catalog = useQuery({ queryKey: ['applications', 'all'], queryFn: allApplications, enabled: !!search.trim() });
  const matching = catalog.data?.filter(app => `${app.name} ${app.code}`.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase())) ?? [];
  const searching = !!search.trim();
  const change = (app: Application) => modal.confirm({ title: `${app.disabledAt ? '恢复' : '禁用'}应用「${app.name}」？`, content: app.disabledAt ? '恢复后客户端需要重新激活，旧会话不会恢复。' : '此应用的卡密将不能激活，现有会话将被撤销。', okText: app.disabledAt ? '确认恢复' : '确认禁用', cancelText: '取消', okButtonProps: { danger: !app.disabledAt }, onOk: async () => {
    try { await api(`/admin/applications/${app.id}/${app.disabledAt ? 'enable' : 'disable'}`, { method: 'POST' }); await cache.invalidateQueries(); message.success('应用状态已更新'); } catch (e) { message.error((e as Error).message); throw e; }
  } });
  return <><PageTitle title="应用管理" subtitle="管理软件应用，选择共用卡种并配置应用策略。" extra={isAdmin && <Button type="primary" icon={<PlusOutlined />} onClick={() => { form.resetFields(); form.setFieldValue('code', 'app_' + crypto.randomUUID().replaceAll('-', '')); setError(null); setCreateOpen(true); }}>创建应用</Button>} />
    <QueryError error={searching ? catalog.error : apps.error} retry={searching ? catalog.refetch : apps.refetch} /><Card className="data-panel">
      <div className="panel-heading"><div><h2 className="panel-title">应用列表 <span className="count-badge">{searching ? matching.length : apps.data?.total ?? '—'}</span></h2><p className="panel-description">各应用独立应用设置与授权，互不影响。</p></div><Button icon={<ReloadOutlined />} onClick={() => searching ? catalog.refetch() : apps.refetch()} loading={searching ? catalog.isFetching && !catalog.isLoading : apps.isFetching && !apps.isLoading}>刷新</Button></div>
      <div className="filter-bar"><div className="filter-field"><label htmlFor="application-search">搜索应用</label><Input id="application-search" value={search} allowClear placeholder="输入应用名称或标识" maxLength={120} onChange={event => { setSearch(event.target.value); setPage(1); }} /></div></div>
      <Table<Application> rowKey="id" loading={searching ? catalog.isLoading : apps.isLoading} dataSource={searching ? matching.slice((page - 1) * 20, page * 20) : apps.data?.items} locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={searching ? '没有匹配的应用，请调整搜索条件。' : isAdmin ? '还没有应用，创建第一个应用开始管理授权。' : '暂无应用，请联系管理员创建。'} /> }} scroll={{ x: 820 }} pagination={{ current: page, total: searching ? matching.length : apps.data?.total, pageSize: 20, onChange: setPage, showSizeChanger: false, showTotal: total => `共 ${total} 个应用` }} columns={[
      { title: '应用名称', dataIndex: 'name', width: 230, render: value => <div className="application-cell"><span className="application-icon"><AppstoreOutlined /></span><strong>{value}</strong></div> }, { title: '应用标识', dataIndex: 'code', render: value => <CopyValue value={value} label="应用标识" compact /> },
      { title: '状态', dataIndex: 'disabledAt', width: 120, render: value => <Tag className="status-tag" icon={value ? <StopOutlined /> : <CheckCircleOutlined />} color={value ? 'error' : 'success'}>{value ? '已禁用' : '运行中'}</Tag> }, { title: '创建时间', dataIndex: 'createdAt', width: 185, render: dateText },
      { title: '操作', width: 170, fixed: screens.md ? 'right' : undefined, render: (_, app) => <Space size={4}><Button type="link" onClick={() => setSelected(app)}>应用设置</Button>{isAdmin && <Button type="link" danger={!app.disabledAt} onClick={() => change(app)}>{app.disabledAt ? '恢复' : '禁用'}</Button>}</Space> },
    ]} /></Card>
    <Modal title="创建应用" open={createOpen} closable={{ 'aria-label': '关闭', disabled: saving }} onCancel={() => { if (!saving) setCreateOpen(false); }} maskClosable={false} cancelButtonProps={{ disabled: saving }} okText="创建" cancelText="取消" confirmLoading={saving} onOk={() => form.submit()} afterClose={() => { if (created) { setSelected(created); setCreated(null); } }}>
      <div className="overlay-intro"><span className="overlay-icon"><AppstoreOutlined /></span><div><strong>建立独立的授权空间</strong><p>创建后可引用已有卡种，为软件签发授权。</p></div></div>
      {error && <Alert type="error" showIcon title={error} className="query-error" />}
      <Form name="create-application" form={form} layout="vertical" requiredMark={false} disabled={saving} onFinish={async (values: { name: string; code: string }) => { if (saving) return; setSaving(true); setError(null); try { const result = await api<Application>('/admin/applications', { method: 'POST', body: { name: values.name.trim(), code: values.code.trim() } }); setCreated(result); setCreateOpen(false); setPage(1); await cache.invalidateQueries({ queryKey: ['applications'] }); await invalidatePlanQueries(cache); message.success('应用已创建，请引用卡种后开始发卡'); } catch (e) { setError((e as Error).message); } finally { setSaving(false); } }}>
      <Form.Item label="应用名称" name="name" rules={[{ required: true, whitespace: true, message: '请输入应用名称' }]}><Input maxLength={120} placeholder="例如：桌面工具专业版" /></Form.Item>
      <Form.Item label="应用标识" name="code" rules={[{ required: true, message: '请输入应用标识' }, { pattern: /^[a-zA-Z0-9_-]{3,64}$/, message: '请输入 3–64 位字母、数字、短横线或下划线' }]} extra="已自动生成，无需填写。也可自定义；客户端使用此标识，创建后不可修改。"><Input placeholder="自动生成的应用标识" maxLength={64} /></Form.Item>
    </Form></Modal><ApplicationSettingsDrawer key={selected?.id ?? 'closed'} application={selected} close={() => setSelected(null)} />
  </>;
}

