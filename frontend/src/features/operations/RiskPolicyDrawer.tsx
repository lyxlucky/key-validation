import { useEffect, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Drawer, Form, InputNumber, Select, Space, Switch } from 'antd';
import { api } from '../../lib/api';
import type { RiskPolicy } from '../../lib/reliability-types';
import { QueryError } from '../../components/common';
import { useOperationsCatalog } from './useOperationsCatalog';
import { useFormSnapshot } from '../../components/useFormSnapshot';

interface Values { enabled: boolean; activationThreshold: number; activationWindowMinutes: number; rebindingThreshold: number; rebindingWindowMinutes: number; regionWindowHours: number }
const fields: [keyof Omit<Values, 'enabled'>, string][] = [['activationThreshold', '激活次数阈值'], ['activationWindowMinutes', '激活统计窗口（分钟）'], ['rebindingThreshold', '换绑次数阈值'], ['rebindingWindowMinutes', '换绑统计窗口（分钟）'], ['regionWindowHours', '地区比较窗口（小时）']];
const maximums: Record<keyof Omit<Values, 'enabled'>, number> = { activationThreshold: 10000, activationWindowMinutes: 1440, rebindingThreshold: 1000, rebindingWindowMinutes: 10080, regionWindowHours: 720 };

export function RiskPolicyDrawer({ open, close }: { open: boolean; close: () => void }) {
  const [applicationId, setApplicationId] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [form] = Form.useForm<Values>();
  const { message } = App.useApp();
  const cache = useQueryClient();
  const catalog = useOperationsCatalog(open);
  const policy = useQuery({ queryKey: ['risk-policy', applicationId], queryFn: () => api<RiskPolicy>(`/admin/applications/${applicationId}/risk-policy`), enabled: open && !!applicationId });
  useEffect(() => { if (!open) { setApplicationId(undefined); setError(null); } }, [open]);
  useFormSnapshot(form, policy.data, open, () => ({ enabled: policy.data!.enabled, ...Object.fromEntries(fields.map(([field]) => [field, Number(policy.data![field])])) }));
  async function save(values: Values) {
    if (!applicationId || saving) return;
    setSaving(true); setError(null);
    try {
      await api(`/admin/applications/${applicationId}/risk-policy`, { method: 'PUT', body: values });
      await cache.invalidateQueries({ queryKey: ['risk-policy', applicationId] });
      message.success('风险策略已保存'); close();
    } catch (caught) { setError((caught as Error).message); }
    finally { setSaving(false); }
  }
  return <Drawer title="风险策略" open={open} onClose={() => { if (!saving) close(); }} size={520} closable={{ 'aria-label': '关闭' }} destroyOnHidden footer={<Space><Button type="primary" htmlType="submit" form="risk-policy-form" loading={saving} disabled={!policy.data || policy.isFetching}>保存策略</Button><Button disabled={saving} onClick={close}>取消</Button></Space>}>
    <div className="filter-field operations-block"><label htmlFor="risk-policy-application">策略应用</label><Select id="risk-policy-application" showSearch={{ optionFilterProp: 'label' }} placeholder="选择应用" disabled={saving} value={applicationId} options={catalog.data?.applications.map(app => ({ value: app.id, label: app.name }))} onChange={setApplicationId} /></div>
    <QueryError error={catalog.error} retry={catalog.refetch} /><QueryError error={policy.error} retry={policy.refetch} />
    {error && <Alert type="error" showIcon title={error} className="query-error" />}
    {policy.data && <Form name="risk-policy" id="risk-policy-form" form={form} layout="vertical" onFinish={save} disabled={saving || policy.isFetching}>
      <Form.Item label="记录异常" name="enabled" valuePropName="checked"><Switch aria-label="记录异常" /></Form.Item>
      {fields.map(([name, label]) => <Form.Item key={name} label={label} name={name} rules={[{ required: true, message: '请输入正整数' }, { type: 'integer', min: 1, max: maximums[name], message: `请输入 1–${maximums[name]} 之间的整数` }]}><InputNumber min={1} max={maximums[name]} precision={0} style={{ width: '100%' }} /></Form.Item>)}
      <Alert type="info" showIcon title="地区判断使用可信代理提供的国家代码" description="未配置可信来源时，地区判断不可用。异常记录交由人工复核，授权状态可在卡密管理中处理。" />
    </Form>}
  </Drawer>;
}
