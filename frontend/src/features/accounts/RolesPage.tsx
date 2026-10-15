import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Checkbox, Form, Input, Modal, Space, Table, Tag } from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { api, queryString } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { Permission, Role } from '../../lib/rbac-types';
import { roleNames } from '../../lib/management-types';
import { emptyTable, PageTitle, QueryError } from '../../components/common';

interface RoleValues { name: string; description?: string; permissions: string[] }
const protectedRole = (role: Role | null) => !!role?.isBuiltIn && role.name === 'Admin';

function PermissionEditor({ value = [], onChange, catalog, disabled }: { value?: string[]; onChange?: (value: string[]) => void; catalog: Permission[]; disabled?: boolean }) {
  return <Checkbox.Group value={value} onChange={onChange} disabled={disabled} className="role-permissions">
    {[...new Set(catalog.map(permission => permission.group))].map(group => <section key={group} className="role-permission-group"><h3>{group}</h3>
      {catalog.filter(permission => permission.group === group).map(permission => <Checkbox key={permission.code} value={permission.code} aria-label={permission.label}>
        <span>{permission.label}</span><small>{permission.description}</small>
      </Checkbox>)}
    </section>)}
  </Checkbox.Group>;
}

export function RolesPage() {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [editor, setEditor] = useState<{ role: Role | null } | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { message, modal } = App.useApp();
  const cache = useQueryClient();
  const catalog = useQuery({ queryKey: ['permission-catalog'], queryFn: () => api<Permission[]>('/admin/permissions') });
  const roles = useQuery({ queryKey: ['roles', page, search], queryFn: () => api<Page<Role>>('/admin/roles' + queryString({ page, pageSize: 20, search })) });
  const readonly = protectedRole(editor?.role ?? null);
  function open(role: Role | null) { setError(null); setEditor({ role }); }
  async function save(values: RoleValues) {
    if (!editor || readonly || saving) return;
    setSaving(true); setError(null);
    try {
      await api(editor.role ? `/admin/roles/${editor.role.id}` : '/admin/roles', { method: editor.role ? 'PATCH' : 'POST', body: { name: values.name.trim(), description: values.description?.trim() || null, permissions: values.permissions ?? [] } });
      setEditor(null);
      await cache.invalidateQueries({ predicate: query => ['roles', 'accounts', 'audit'].includes(String(query.queryKey[0])) });
      message.success('角色已保存，受影响账号需要重新登录');
    } catch (caught) { setError((caught as Error).message); }
    finally { setSaving(false); }
  }
  function remove(role: Role) {
    modal.confirm({ title: `删除角色「${role.name}」？`, content: '仅未分配给任何账号的角色可以删除。', okText: '删除角色', cancelText: '取消', okButtonProps: { danger: true }, onOk: async () => {
      try { await api(`/admin/roles/${role.id}`, { method: 'DELETE' }); await cache.invalidateQueries({ queryKey: ['roles'] }); message.success('角色已删除'); }
      catch (caught) { message.error((caught as Error).message); throw caught; }
    } });
  }
  return <><PageTitle title="角色管理" subtitle="按职责定义操作权限，为账号分配一个或多个角色。" extra={<Button type="primary" icon={<PlusOutlined aria-hidden />} onClick={() => open(null)}>创建角色</Button>} />
    <QueryError error={roles.error} retry={roles.refetch} /><QueryError error={catalog.error} retry={catalog.refetch} />
    <Card className="data-panel"><div className="panel-heading"><div><h2 className="panel-title">角色与权限</h2><p className="panel-description">账号获得所有角色权限的并集，应用和渠道的数据范围单独设置。</p></div><Button aria-label="刷新角色" icon={<ReloadOutlined />} onClick={() => roles.refetch()}>刷新</Button></div>
      <div className="filter-bar"><Input.Search aria-label="搜索角色" placeholder="搜索角色名称" allowClear maxLength={128} onSearch={value => { setSearch(value.trim()); setPage(1); }} style={{ maxWidth: 340 }} /></div>
      <Table<Role> rowKey="id" loading={roles.isLoading} dataSource={roles.data?.items} locale={{ emptyText: emptyTable }} scroll={{ x: 880 }} pagination={{ current: page, pageSize: 20, total: roles.data?.total, showSizeChanger: false, onChange: setPage, showTotal: total => `共 ${total} 个角色` }} columns={[
        { title: '角色名称', dataIndex: 'name', width: 200, render: (name, role) => <Space wrap><strong>{roleNames[name] ?? name}</strong>{role.isBuiltIn && <Tag>内置</Tag>}</Space> },
        { title: '用途说明', dataIndex: 'description', width: 220, render: value => value || '未填写说明' },
        { title: '操作权限', render: (_, role) => <div className="role-grant-summary">{role.permissions.length ? role.permissions.map(code => <Tag key={code}>{catalog.data?.find(permission => permission.code === code)?.label ?? code}</Tag>) : <span className="muted">未授予操作权限</span>}</div> },
        { title: '成员', dataIndex: 'userCount', width: 65 },
        { title: '操作', width: 150, render: (_, role) => <Space size={0}><Button type="link" onClick={() => open(role)}>{protectedRole(role) ? '查看权限' : '编辑角色'}</Button>{!protectedRole(role) && <Button type="link" danger disabled={Number(role.userCount) > 0} onClick={() => remove(role)}>删除</Button>}</Space> },
      ]} />
    </Card>
    <Modal title={editor?.role ? readonly ? '查看角色权限' : '编辑角色' : '创建角色'} open={!!editor} onCancel={() => { if (!saving) setEditor(null); }} closable={{ 'aria-label': '关闭', disabled: saving }} width={720} destroyOnHidden
      footer={readonly ? <Button onClick={() => setEditor(null)}>关闭</Button> : undefined} okText="保存角色" cancelText="取消" cancelButtonProps={{ disabled: saving }} maskClosable={false} confirmLoading={saving} okButtonProps={{ htmlType: 'submit', form: 'role-editor-form', disabled: !catalog.data }}>
      {readonly ? <Alert type="info" showIcon className="query-error" title="内置管理员角色受保护，拥有全部权限。" /> : editor?.role && <Alert type="info" showIcon className="query-error" title={`保存变更后，该角色的 ${editor.role.userCount} 个成员需重新登录。`} />}
      {error && <Alert type="error" showIcon title={error} className="query-error" />}
      {editor && <Form key={editor.role?.id ?? 'new'} id="role-editor-form" name="role-editor" layout="vertical" disabled={saving || readonly} initialValues={editor.role ?? { name: '', description: '', permissions: [] }} onFinish={save}>
        <Form.Item label="角色名称" name="name" rules={[{ required: true, whitespace: true, message: '请输入角色名称' }, { max: 64, message: '最多 64 个字符' }]}><Input maxLength={64} /></Form.Item>
        <Form.Item label="角色说明" name="description"><Input.TextArea maxLength={500} autoSize={{ minRows: 2, maxRows: 4 }} /></Form.Item>
        <Form.Item label="操作权限" name="permissions" extra="未勾选的操作不会授予；空角色不授予任何操作权限。"><PermissionEditor catalog={catalog.data ?? []} disabled={saving || readonly} /></Form.Item>
      </Form>}
    </Modal>
  </>;
}
