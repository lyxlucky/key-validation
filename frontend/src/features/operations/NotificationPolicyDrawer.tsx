import { useEffect, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Checkbox, Drawer, Form, Select, Space, Switch } from 'antd';
import { api } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { Account } from '../../lib/management-types';
import type { NotificationPolicy } from '../../lib/reliability-types';
import { eventOptions } from '../../lib/reliability-types';
import { QueryError } from '../../components/common';
import { useOperationsCatalog } from './useOperationsCatalog';
import { useFormSnapshot } from '../../components/useFormSnapshot';

interface Values { enabled: boolean; recipientIds: string[]; eventTypes: string[]; expiryDays: number[] }
async function enabledAccounts() {
  const accounts: Account[] = [];
  for (let page = 1; ; page++) {
    const result = await api<Page<Account>>(`/admin/accounts?page=${page}&pageSize=100`);
    accounts.push(...result.items);
    if (!result.items.length || accounts.length >= result.total) return accounts.filter(account => !account.disabledAt);
  }
}

export function NotificationPolicyDrawer({ open, close }: { open: boolean; close: () => void }) {
  const [applicationId, setApplicationId] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [form] = Form.useForm<Values>();
  const { message } = App.useApp();
  const cache = useQueryClient();
  const catalog = useOperationsCatalog(open);
  const accounts = useQuery({ queryKey: ['accounts', 'notification-options'], queryFn: enabledAccounts, enabled: open });
  const policy = useQuery({ queryKey: ['notification-policy', applicationId], queryFn: () => api<NotificationPolicy>(`/admin/applications/${applicationId}/notification-policy`), enabled: open && !!applicationId });
  useEffect(() => { if (!open) { setApplicationId(undefined); setError(null); } }, [open]);
  useFormSnapshot(form, policy.data, open, () => ({ ...policy.data!, expiryDays: policy.data!.expiryDays.map(Number) }));
  async function save(values: Values) {
    if (!applicationId || saving) return;
    setSaving(true); setError(null);
    try {
      await api(`/admin/applications/${applicationId}/notification-policy`, { method: 'PUT', body: { enabled: values.enabled, recipientIds: values.recipientIds ?? [], eventTypes: values.eventTypes ?? [], expiryDays: values.expiryDays ?? [] } });
      await cache.invalidateQueries({ queryKey: ['notification-policy', applicationId] });
      message.success('通知设置已保存'); close();
    } catch (caught) { setError((caught as Error).message); }
    finally { setSaving(false); }
  }
  return <Drawer title="通知设置" open={open} onClose={() => { if (!saving) close(); }} size={560} closable={{ 'aria-label': '关闭' }} destroyOnHidden footer={<Space><Button type="primary" htmlType="submit" form="notification-policy-form" loading={saving} disabled={!policy.data || policy.isFetching}>保存设置</Button><Button disabled={saving} onClick={close}>取消</Button></Space>}>
    <div className="filter-field operations-block"><label htmlFor="notification-policy-application">通知应用</label><Select id="notification-policy-application" showSearch={{ optionFilterProp: 'label' }} placeholder="选择应用" disabled={saving} value={applicationId} options={catalog.data?.applications.map(app => ({ value: app.id, label: app.name }))} onChange={setApplicationId} /></div>
    <QueryError error={catalog.error} retry={catalog.refetch} /><QueryError error={accounts.error} retry={accounts.refetch} /><QueryError error={policy.error} retry={policy.refetch} />
    {error && <Alert type="error" showIcon title={error} className="query-error" />}
    {policy.data && <Form id="notification-policy-form" name="notification-policy" form={form} layout="vertical" onFinish={save} disabled={saving || policy.isFetching}>
      <Form.Item label="启用通知" name="enabled" valuePropName="checked"><Switch aria-label="启用通知" /></Form.Item>
      <Form.Item label="接收账号" name="recipientIds" extra="接收账号须保持启用，并有权访问此应用。"><Select mode="multiple" showSearch={{ optionFilterProp: 'label' }} options={accounts.data?.map(account => ({ value: account.id, label: account.userName }))} placeholder="选择接收账号" /></Form.Item>
      <Form.Item label="通知事件" name="eventTypes"><Checkbox.Group className="permission-options" options={eventOptions} /></Form.Item>
      <Form.Item label="提前提醒" name="expiryDays" extra="按授权当前到期时间提醒；续期后可产生新的提醒。"><Checkbox.Group options={[{ value: 3, label: '3 天' }, { value: 7, label: '7 天' }, { value: 30, label: '30 天' }]} /></Form.Item>
      <Alert type="info" showIcon title="站内通知按接收账号独立记录已读状态" description="需要对接外部系统时，可在开放集成中配置 Webhook。" />
    </Form>}
  </Drawer>;
}
