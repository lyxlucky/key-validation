import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Drawer, Form, Select, Space, Spin, Switch, Tag } from 'antd';
import { api } from '../../lib/api';
import type { Account } from '../../lib/management-types';
import { permissionNames } from '../../lib/operations-types';
import type { AccessProfile } from '../../lib/operations-types';
import { QueryError } from '../../components/common';
import { useFormSnapshot } from '../../components/useFormSnapshot';
import { useOperationsCatalog } from '../operations/useOperationsCatalog';

export function AccountAccessDrawer({ account, close }: { account: Account | null; close: () => void }) {
  const [form] = Form.useForm<AccessProfile>();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { message } = App.useApp();
  const cache = useQueryClient();
  const allApplicationsSelected = Form.useWatch('allApplications', form) ?? true;
  const access = useQuery({ queryKey: ['account-access', account?.id], queryFn: () => api<AccessProfile>(`/admin/accounts/${account!.id}/access`), enabled: !!account, refetchOnWindowFocus: false });
  const apps = useOperationsCatalog(!!account);
  useFormSnapshot(form, access.data, !!account, () => access.data!);
  async function save(values: AccessProfile) {
    if (!account || saving) return;
    setSaving(true); setError(null);
    try {
      await api(`/admin/accounts/${account.id}/access`, { method: 'PUT', body: { allApplications: values.allApplications, applicationIds: values.allApplications ? [] : values.applicationIds ?? [] } });
      await cache.invalidateQueries({ predicate: query => ['account-access', 'accounts', 'audit'].includes(String(query.queryKey[0])) });
      message.success('数据范围已保存，该账号的旧登录会话已失效'); close();
    } catch (caught) { setError((caught as Error).message); }
    finally { setSaving(false); }
  }
  return <Drawer title="应用与渠道范围" open={!!account} size={540} onClose={() => { if (!saving) { setError(null); close(); } }} closable={{ 'aria-label': '关闭' }} destroyOnHidden footer={<Space><Button type="primary" htmlType="submit" form="account-access-form" loading={saving} disabled={!access.data}>保存范围</Button><Button disabled={saving} onClick={() => { setError(null); close(); }}>取消</Button></Space>}>
    <p className="panel-description">账号：<strong>{account?.userName}</strong></p>
    <QueryError error={access.error} retry={access.refetch} /><QueryError error={apps.error} retry={apps.refetch} />
    {error && <Alert type="error" showIcon title={error} className="query-error" />}
    {access.isLoading ? <Spin /> : access.data && <Form id="account-access-form" name="account-access" form={form} layout="vertical" onFinish={save} disabled={saving}>
      <p className="panel-description">操作权限由已分配角色决定。</p><div className="role-grant-summary">{access.data.permissions.map(code => <Tag key={code}>{permissionNames[code] ?? code}</Tag>)}</div>
      <Form.Item label="全部应用" name="allApplications" valuePropName="checked"><Switch aria-label="全部应用" /></Form.Item>
      {!allApplicationsSelected && <Form.Item label="允许访问的应用" name="applicationIds" extra="未选择应用时，该账号无法访问任何应用数据。"><Select mode="multiple" showSearch={{ optionFilterProp: 'label' }} options={apps.data?.applications.map(app => ({ value: app.id, label: app.name }))} placeholder="选择应用" /></Form.Item>}
      {access.data.channelId && <Alert type="info" showIcon title="此账号同时受所属渠道的数据范围限制。" />}
    </Form>}
  </Drawer>;
}
