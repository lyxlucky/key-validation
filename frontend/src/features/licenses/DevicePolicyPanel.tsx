import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Descriptions, Form, Input, InputNumber, Modal, Space, Switch, Tag } from 'antd';
import { api } from '../../lib/api';
import type { Dto } from '../../lib/types';
import { dateText, QueryError } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';

type DevicePolicy = Dto<'DevicePolicyDto'>;
export function DevicePolicyPanel({ licenseId }: { licenseId: string }) {
  const { canManage } = useAuth();
  const { message } = App.useApp();
  const cache = useQueryClient();
  const policy = useQuery({ queryKey: ['device-policy', licenseId], queryFn: () => api<DevicePolicy>(`/admin/licenses/${licenseId}/device-policy`) });
  const [editor, setEditor] = useState<'edit' | 'reset' | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  async function save(values: { enabled: boolean; maxChanges: number; windowDays: number; cooldownHours: number; reason: string }) {
    if (busy || !editor) return;
    setBusy(true); setError(null);
    try {
      await api(`/admin/licenses/${licenseId}/device-policy${editor === 'reset' ? '/reset' : ''}`, { method: editor === 'reset' ? 'POST' : 'PATCH', body: editor === 'reset' ? { reason: values.reason.trim() } : { ...values, reason: values.reason.trim() } });
      setEditor(null); await cache.invalidateQueries({ predicate: query => ['device-policy', 'audit'].includes(String(query.queryKey[0])) }); message.success('换绑规则已更新');
    } catch (caught) { setError((caught as Error).message); }
    finally { setBusy(false); }
  }
  return <section className="device-policy"><h3 className="section-heading">设备换绑</h3><QueryError error={policy.error} retry={policy.refetch} />{policy.data && <>
    <Descriptions size="small" column={1} items={[
      { key: 'status', label: '换绑限制', children: <Tag color={policy.data.enabled ? 'blue' : 'default'}>{policy.data.enabled ? '已启用' : '不限制'}</Tag> },
      { key: 'quota', label: '计数窗口', children: `滚动 ${policy.data.windowDays} 天，已用 ${policy.data.usedChanges} 次${policy.data.enabled ? `，剩余 ${policy.data.remainingChanges} 次` : ''}` },
      { key: 'cooldown', label: '冷却时间', children: `${policy.data.cooldownHours} 小时` },
      { key: 'last', label: '上次释放设备', children: dateText(policy.data.lastChangedAt) },
      { key: 'next', label: '下次允许时间', children: policy.data.nextAllowedAt ? dateText(policy.data.nextAllowedAt) : '当前可以换绑' },
    ]} /><p className="form-note">成功释放一条设备绑定计为一次换绑，会撤销该设备会话；有效期保持不变。</p>
    {canManage && <Space className="device-policy-actions" wrap size={12}><Button onClick={() => { setError(null); setEditor('edit'); }}>设置换绑规则</Button><Button onClick={() => { setError(null); setEditor('reset'); }}>重置次数与冷却</Button></Space>}
    <Modal title={editor === 'reset' ? '重置换绑次数与冷却' : '设置换绑规则'} open={!!editor} onCancel={() => { if (!busy) setEditor(null); }} closable={{ 'aria-label': '关闭', disabled: busy }} okText="保存规则" cancelText="取消" cancelButtonProps={{ disabled: busy }} maskClosable={false} confirmLoading={busy} okButtonProps={{ htmlType: 'submit', form: 'device-policy-form' }} destroyOnHidden>
      {error && <Alert type="error" showIcon title={error} className="query-error" />}
      <Alert type={editor === 'reset' ? 'warning' : 'info'} showIcon className="query-error" title={editor === 'reset' ? '重置后可立即重新换绑，历史记录保留' : '规则仅适用于这张卡密，修改规则不会清空已用次数'} description="不会释放现有设备、延长有效期或恢复旧会话。" />
      {editor && <Form key={editor} id="device-policy-form" name="device-policy" initialValues={{ ...policy.data, reason: '' }} layout="vertical" onFinish={save} disabled={busy} requiredMark={false}>
        {editor === 'edit' && <><Form.Item name="enabled" label="启用换绑限制" valuePropName="checked"><Switch /></Form.Item><div className="form-grid"><Form.Item name="maxChanges" label="窗口内最多次数" rules={[{ required: true, type: 'integer', min: 1, max: 100 }]}><InputNumber min={1} max={100} precision={0} /></Form.Item><Form.Item name="windowDays" label="滚动窗口（天）" rules={[{ required: true, type: 'integer', min: 1, max: 365 }]}><InputNumber min={1} max={365} precision={0} /></Form.Item></div><Form.Item name="cooldownHours" label="冷却时间（小时）" rules={[{ required: true, type: 'integer', min: 0, max: 8760 }]}><InputNumber min={0} max={8760} precision={0} /></Form.Item></>}
        <Form.Item name="reason" label="调整原因" rules={[{ required: true, whitespace: true, message: '请填写原因' }, { max: 500 }]}><Input.TextArea rows={2} maxLength={500} /></Form.Item>
      </Form>}
    </Modal>
  </>}</section>;
}
