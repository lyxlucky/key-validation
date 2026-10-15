import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Alert, App, Checkbox, Form, Input, Modal, Select, Switch } from 'antd';
import { api } from '../../lib/api';
import type { WebhookSecret, WebhookSubscription } from '../../lib/reliability-types';
import { eventOptions } from '../../lib/reliability-types';
import { QueryError } from '../../components/common';
import { useOperationsCatalog } from '../operations/useOperationsCatalog';
import { useFormSnapshot } from '../../components/useFormSnapshot';

interface Values { applicationId: string; name: string; url: string; eventTypes: string[]; isEnabled: boolean }

export function WebhookEditor({ open, subscription, close, showSecret }: { open: boolean; subscription: WebhookSubscription | null; close: () => void; showSecret: (secret: string) => void }) {
  const [form] = Form.useForm<Values>();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const pendingSecret = useRef<string | null>(null);
  const catalog = useOperationsCatalog(open);
  const cache = useQueryClient();
  const { message } = App.useApp();
  useEffect(() => { if (!open) setError(null); }, [open]);
  useFormSnapshot(form, subscription ?? 'new', open, () => subscription ?? { applicationId: undefined, name: '', url: '', eventTypes: ['license.expiring', 'risk.detected'], isEnabled: true });
  async function save(values: Values) {
    if (saving) return;
    setSaving(true); setError(null);
    try {
      const body = { name: values.name.trim(), url: values.url.trim(), eventTypes: values.eventTypes, isEnabled: values.isEnabled };
      if (subscription) await api(`/admin/webhook-subscriptions/${subscription.id}`, { method: 'PATCH', body });
      else {
        const result = await api<WebhookSecret>('/admin/webhook-subscriptions', { method: 'POST', body: { ...body, applicationId: values.applicationId } });
        pendingSecret.current = result.secret;
      }
      await cache.invalidateQueries({ queryKey: ['webhook-subscriptions'] });
      close(); message.success('Webhook 订阅已保存');
    } catch (caught) { setError((caught as Error).message); }
    finally { setSaving(false); }
  }
  return <Modal title={subscription ? '编辑 Webhook 订阅' : '创建 Webhook 订阅'} open={open} onCancel={() => { if (!saving) close(); }} closable={{ 'aria-label': '关闭', disabled: saving }} maskClosable={false} cancelButtonProps={{ disabled: saving }} confirmLoading={saving} okText="保存订阅" cancelText="取消" okButtonProps={{ htmlType: 'submit', form: 'webhook-editor-form' }} width={600} destroyOnHidden afterClose={() => { if (pendingSecret.current) { const value = pendingSecret.current; pendingSecret.current = null; showSecret(value); } }}>
    <QueryError error={catalog.error} retry={catalog.refetch} />{error && <Alert type="error" showIcon title={error} className="query-error" />}
    <Form id="webhook-editor-form" name="webhook-editor" form={form} layout="vertical" onFinish={save} disabled={saving}>
      <Form.Item label="所属应用" name="applicationId" rules={[{ required: true, message: '请选择应用' }]}><Select disabled={!!subscription || saving} showSearch={{ optionFilterProp: 'label' }} options={catalog.data?.applications.map(app => ({ value: app.id, label: app.name }))} placeholder="选择应用" /></Form.Item>
      <Form.Item label="订阅名称" name="name" rules={[{ required: true, whitespace: true, message: '请输入订阅名称' }]}><Input maxLength={120} /></Form.Item>
      <Form.Item label="接收地址" name="url" rules={[{ required: true, message: '请输入接收地址' }, { type: 'url', message: '请输入完整网址' }]} extra="生产接收地址须使用 HTTPS，且主机在允许清单内。本机开发例外需单独配置。"><Input maxLength={2000} placeholder="https://your-service.example/events" /></Form.Item>
      <Form.Item label="接收事件" name="eventTypes" rules={[{ required: true, type: 'array', min: 1, message: '至少选择一种事件' }]}><Checkbox.Group className="permission-options" options={eventOptions} /></Form.Item>
      <Form.Item label="启用订阅" name="isEnabled" valuePropName="checked"><Switch aria-label="启用订阅" /></Form.Item>
    </Form>
  </Modal>;
}
