import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Navigate } from 'react-router-dom';
import { Alert, App, Button, Card, Form, Input, Select, Space, Table, Tag } from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { api, queryString } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { Account } from '../../lib/management-types';
import { roleNames } from '../../lib/management-types';
import { allRoles } from '../../lib/rbac-types';
import { dateText, emptyTable, PageTitle, QueryError } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';
import { AccountAccessDrawer } from './AccountAccessDrawer';
import { LiveModal } from '../../components/LiveModal';

type Editor = { kind: 'create' } | { kind: 'role' | 'password'; account: Account };
export function AccountsPage() {
  const { user, isAdmin } = useAuth();
  const [form] = Form.useForm();
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState('');
  const [editor, setEditor] = useState<Editor | null>(null);
  const [accessAccount, setAccessAccount] = useState<Account | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { modal, message } = App.useApp();
  const cache = useQueryClient();
  const accounts = useQuery({ queryKey: ['accounts', page, filter], queryFn: () => api<Page<Account>>('/admin/accounts' + queryString({ page, pageSize: 20, search: filter })), enabled: isAdmin });
  const roles = useQuery({ queryKey: ['roles', 'all'], queryFn: allRoles, enabled: isAdmin });
  if (!isAdmin) return <Navigate to="/overview" replace />;
  function open(next: Editor) { form.resetFields(); form.setFieldsValue({ roleIds: next.kind === 'role' ? next.account.roleIds : [] }); setError(null); setEditor(next); }
  function close() { if (!saving) { form.resetFields(); setEditor(null); setError(null); } }
  function changeState(account: Account) {
    modal.confirm({ title: `${account.disabledAt ? '恢复' : '停用'}账号「${account.userName}」？`, content: account.disabledAt ? '恢复后可以重新登录，旧会话不会恢复。' : '账号将无法登录，已有登录会话立即失效。审计记录仍然保留。', okText: account.disabledAt ? '确认恢复账号' : '确认停用账号', cancelText: '取消', okButtonProps: { danger: !account.disabledAt }, onOk: async () => {
      try { await api(`/admin/accounts/${account.id}/${account.disabledAt ? 'enable' : 'disable'}`, { method: 'POST' }); await cache.invalidateQueries({ predicate: query => ['accounts', 'audit'].includes(String(query.queryKey[0])) }); message.success('账号状态已更新'); } catch (caught) { message.error((caught as Error).message); throw caught; }
    } });
  }
  async function save(values: { userName: string; password: string; roleIds: string[] }) {
    if (!editor || saving) return;
    setSaving(true); setError(null);
    try {
      if (editor.kind === 'create') await api('/admin/accounts', { method: 'POST', body: { userName: values.userName.trim(), password: values.password, roleIds: values.roleIds } });
      else if (editor.kind === 'role') await api(`/admin/accounts/${editor.account.id}/roles`, { method: 'PATCH', body: { roleIds: values.roleIds } });
      else await api(`/admin/accounts/${editor.account.id}/reset-password`, { method: 'POST', body: { password: values.password } });
      if (editor.kind === 'create') { setPage(1); setSearch(''); setFilter(''); }
      form.resetFields(); setEditor(null); await cache.invalidateQueries({ predicate: query => ['accounts', 'roles', 'audit'].includes(String(query.queryKey[0])) }); message.success('账号已更新');
    } catch (caught) { setError((caught as Error).message); }
    finally { setSaving(false); }
  }
  return <><PageTitle title="账号管理" subtitle="分配角色与数据范围，维护账号状态和登录安全。" extra={<Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => open({ kind: 'create' })}>创建账号</Button>} /><QueryError error={roles.error} retry={roles.refetch} />
    <QueryError error={accounts.error} retry={accounts.refetch} /><Card className="data-panel"><div className="panel-heading"><div><h2 className="panel-title">后台账号 <span className="count-badge">{accounts.data?.total ?? '—'}</span></h2><p className="panel-description">角色或密码变更、账号停用会立即使旧登录会话失效。</p></div><Button icon={<ReloadOutlined />} onClick={() => accounts.refetch()}>刷新</Button></div>
      <div className="filter-bar management-filter-bar"><div className="filter-field"><label htmlFor="account-search">用户名搜索</label><Input.Search id="account-search" value={search} maxLength={128} allowClear placeholder="搜索用户名" onChange={event => setSearch(event.target.value)} onSearch={value => { setFilter(value.trim()); setPage(1); }} enterButton="搜索" /></div></div>
      <Table<Account> rowKey="id" loading={accounts.isLoading} dataSource={accounts.data?.items} locale={{ emptyText: emptyTable }} scroll={{ x: 920 }} pagination={{ current: page, pageSize: 20, total: accounts.data?.total, onChange: setPage, showSizeChanger: false, showTotal: total => `共 ${total} 个账号` }} columns={[
        { title: '用户名', dataIndex: 'userName', render: (name, account) => <Space><strong>{name}</strong>{account.id === user?.id && <Tag>当前账号</Tag>}</Space> },
        { title: '角色', dataIndex: 'roles', render: roles => (roles as string[]).map(role => <Tag key={role}>{roleNames[role] ?? role}</Tag>) },
        { title: '状态', dataIndex: 'disabledAt', render: (value, account) => <Space wrap><Tag color={value ? 'error' : 'success'}>{value ? '已停用' : '已启用'}</Tag>{account.mustChangePassword && <Tag color="warning">待改密</Tag>}{account.twoFactorEnabled && <Tag color="blue">双重验证</Tag>}</Space> },
        { title: '创建时间', dataIndex: 'createdAt', width: 185, render: dateText },
        { title: '操作', width: 330, render: (_, account) => <Space size={4} wrap><Button type="link" disabled={account.id === user?.id} onClick={() => open({ kind: 'role', account })}>调整角色</Button><Button type="link" onClick={() => setAccessAccount(account)}>数据范围</Button><Button type="link" onClick={() => open({ kind: 'password', account })}>重置密码</Button><Button type="link" danger={!account.disabledAt} disabled={account.id === user?.id} onClick={() => changeState(account)}>{account.disabledAt ? '恢复账号' : '停用账号'}</Button></Space> },
      ]} />
    </Card>
    <LiveModal title={editor?.kind === 'create' ? '创建账号' : editor?.kind === 'role' ? '调整角色' : '重置密码'} open={!!editor} closable={{ 'aria-label': '关闭', disabled: saving }} onCancel={close} okButtonProps={{ htmlType: 'submit', form: 'account-editor-form' }} okText={editor?.kind === 'create' ? '创建账号' : '保存修改'} cancelText="取消" cancelButtonProps={{ disabled: saving }} maskClosable={false} confirmLoading={saving}>
      {editor && editor.kind !== 'create' && <p className="panel-description">账号：<strong>{editor.account.userName}</strong></p>}
      {editor?.kind === 'password' && <Alert className="query-error" type="warning" showIcon title="保存后该账号的所有旧登录会话立即失效。" />}
      {error && <Alert className="query-error" type="error" showIcon title={error} />}
      {/* The retained page owns the store even if dialog content reconnects.
          Actual close/save resets it; Activity cleanup must not reset a draft. */}
      {editor && <Form form={form} id="account-editor-form" name="account-editor" initialValues={{ roleIds: [] }} layout="vertical" requiredMark={false} onFinish={save} disabled={saving}>
        {editor?.kind === 'create' && <Form.Item label="用户名" name="userName" rules={[{ required: true, whitespace: true, message: '请输入用户名' }, { pattern: /^[a-zA-Z0-9_.@+-]+$/, message: '使用字母、数字或 - . _ @ + 符号' }]}><Input maxLength={128} autoComplete="off" /></Form.Item>}
        {editor?.kind !== 'role' && <Form.Item label={editor?.kind === 'create' ? '初始密码' : '新密码'} name="password" rules={[{ required: true, message: '请输入密码' }, { min: 12, message: '密码至少 12 位' }, { pattern: /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).+$/, message: '密码需包含大小写字母、数字和符号' }]} extra="至少 12 位，包含大小写字母、数字和符号。保存后不会回显。"><Input.Password maxLength={1024} autoComplete="new-password" /></Form.Item>}
        {editor?.kind !== 'password' && <Form.Item label="账号角色" name="roleIds" rules={[{ required: true, type: 'array', min: 1, max: 20, message: '请选择 1–20 个角色' }]} extra="账号获得这些角色的全部操作权限，应用与渠道范围单独设置。"><Select mode="multiple" showSearch={{ optionFilterProp: 'label' }} loading={roles.isLoading} options={roles.data?.map(role => ({ value: role.id, label: roleNames[role.name] ?? role.name }))} /></Form.Item>}
      </Form>}
    </LiveModal>
    <AccountAccessDrawer account={accessAccount} close={() => setAccessAccount(null)} />
  </>;
}
