import { useEffect, useState } from 'react';
import { useFormSnapshot } from '../../components/useFormSnapshot';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Collapse, Descriptions, Form, Input, Modal, Radio, Select, Space, Table, Tag } from 'antd';
import { ReloadOutlined, ScanOutlined, SettingOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { api, queryString } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { OperationsScan, RiskEvent } from '../../lib/reliability-types';
import { riskRuleNames, riskStatusNames } from '../../lib/reliability-types';
import { dateText, emptyTable, PageTitle, QueryError } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';
import { useOperationsCatalog } from './useOperationsCatalog';
import { RiskPolicyDrawer } from './RiskPolicyDrawer';

export function RiskPage() {
  const [page, setPage] = useState(1);
  const [applicationId, setApplicationId] = useState<string>();
  const [status, setStatus] = useState<string>();
  const [policyOpen, setPolicyOpen] = useState(false);
  const [selected, setSelected] = useState<RiskEvent | null>(null);
  const [saving, setSaving] = useState(false);
  const [scanning, setScanning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [form] = Form.useForm<{ status: string; reason: string }>();
  const { isAdmin, hasPermission } = useAuth();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const cache = useQueryClient();
  const catalog = useOperationsCatalog();
  const events = useQuery({ queryKey: ['risk-events', page, applicationId, status], queryFn: () => api<Page<RiskEvent>>('/admin/risk-events' + queryString({ page, pageSize: 20, applicationId, status })), refetchInterval: 60000 });
  useEffect(() => { if (events.data) setPage(current => Math.min(current, Math.max(1, Math.ceil(events.data.total / 20)))); }, [events.data]);
  useFormSnapshot(form, selected, !!selected, () => ({ status: selected!.status === 'dismissed' ? 'dismissed' : 'confirmed', reason: '' }));
  const appName = (id: string) => catalog.data?.applications.find(app => app.id === id)?.name ?? id;
  async function scan() {
    setScanning(true);
    try {
      const result = await api<OperationsScan>('/admin/operations/scan', { method: 'POST' });
      await cache.invalidateQueries({ predicate: query => ['risk-events', 'notifications', 'webhook-deliveries'].includes(String(query.queryKey[0])) });
      message.success(`扫描完成，新增 ${result.riskEventsCreated} 条异常、${result.notificationsCreated} 条通知`);
    } catch (caught) { message.error((caught as Error).message); }
    finally { setScanning(false); }
  }
  async function review(values: { status: string; reason: string }) {
    if (!selected || saving) return;
    setSaving(true); setError(null);
    try {
      await api(`/admin/risk-events/${selected.id}/review`, { method: 'POST', body: { status: values.status, reason: values.reason.trim() } });
      await cache.invalidateQueries({ queryKey: ['risk-events'] });
      setSelected(null); message.success('复核结果已保存');
    } catch (caught) { setError((caught as Error).message); }
    finally { setSaving(false); }
  }
  return <><PageTitle title="异常检测" subtitle="查看重复激活、频繁换绑与可信地区变化，记录人工复核结果。" extra={<Space wrap><Button icon={<ReloadOutlined aria-hidden />} loading={events.isFetching} onClick={() => events.refetch()}>刷新异常</Button>{isAdmin && <><Button icon={<SettingOutlined aria-hidden />} onClick={() => setPolicyOpen(true)}>风险策略</Button><Button type="primary" icon={<ScanOutlined aria-hidden />} loading={scanning} onClick={scan}>立即扫描</Button></>}</Space>} />
    <QueryError error={catalog.error} retry={catalog.refetch} /><QueryError error={events.error} retry={events.refetch} />
    <Card className="data-panel"><div className="operations-content operations-filter">
      <div className="filter-field"><label htmlFor="risk-application">风险应用</label><Select id="risk-application" allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="全部可见应用" value={applicationId} options={catalog.data?.applications.map(app => ({ value: app.id, label: app.name }))} onChange={value => { setApplicationId(value); setPage(1); }} /></div>
      <div className="filter-field"><label htmlFor="risk-status">复核状态</label><Select id="risk-status" allowClear placeholder="全部状态" value={status} options={Object.entries(riskStatusNames).map(([value, label]) => ({ value, label }))} onChange={value => { setStatus(value); setPage(1); }} /></div>
    </div><Table<RiskEvent> rowKey="id" loading={events.isLoading} dataSource={events.data?.items} locale={{ emptyText: emptyTable }} scroll={{ x: 960 }} pagination={{ current: page, pageSize: 20, total: events.data?.total, onChange: setPage, showSizeChanger: false }} columns={[
      { title: '应用', dataIndex: 'applicationId', render: appName }, { title: '异常规则', dataIndex: 'rule', render: value => riskRuleNames[value] ?? value },
      { title: '证据摘要', dataIndex: 'summary', width: 260 }, { title: '状态', dataIndex: 'status', render: value => <Tag color={value === 'open' ? 'warning' : value === 'confirmed' ? 'error' : 'default'}>{riskStatusNames[value] ?? value}</Tag> },
      { title: '发现时间', dataIndex: 'createdAt', render: dateText }, { title: '操作', width: 90, fixed: 'right', render: (_, event) => <Button type="link" onClick={() => { setSelected(event); setError(null); }}>复核</Button> },
    ]} /></Card>
    <Modal title="复核异常" open={!!selected} onCancel={() => { if (!saving) setSelected(null); }} closable={{ 'aria-label': '关闭', disabled: saving }} cancelButtonProps={{ disabled: saving }} maskClosable={false} confirmLoading={saving} okText="保存复核" cancelText="取消" okButtonProps={{ htmlType: 'submit', form: 'risk-review-form' }} width={640} destroyOnHidden>
      {error && <Alert type="error" showIcon title={error} className="query-error" />}
      {selected && <><Descriptions className="operations-block" bordered column={1} items={[
        { key: 'app', label: '应用', children: appName(selected.applicationId) }, { key: 'license', label: '授权', children: <Space wrap><code className="break-word">{selected.licenseId}</code>{hasPermission('licenses.read') && <Button type="link" disabled={saving} onClick={() => { const id = selected.licenseId; setSelected(null); navigate(`/licenses?licenseId=${id}`); }}>查看授权</Button>}</Space> },
        { key: 'rule', label: '规则', children: riskRuleNames[selected.rule] ?? selected.rule }, { key: 'window', label: '统计窗口', children: `${dateText(selected.windowStart)} 至 ${dateText(selected.windowEnd)}` },
        { key: 'summary', label: '检测结果', children: selected.summary }, ...(selected.reviewedAt ? [{ key: 'previous', label: '上次复核', children: `${dateText(selected.reviewedAt)} · ${riskStatusNames[selected.status]} · ${selected.reviewReason}` }] : []),
      ]} /><Collapse className="operations-block" items={[{ key: 'evidence', label: '检测证据', children: <pre className="operations-secret">{selected.evidenceJson}</pre> }]} />
      <Form id="risk-review-form" name="risk-review" form={form} layout="vertical" onFinish={review} disabled={saving}>
        <Form.Item label="复核结论" name="status" rules={[{ required: true }]}><Radio.Group options={[{ value: 'confirmed', label: '确认异常' }, { value: 'dismissed', label: '排除异常' }]} /></Form.Item>
        <Form.Item label="复核原因" name="reason" rules={[{ required: true, whitespace: true, message: '请填写复核原因' }]}><Input.TextArea maxLength={500} rows={3} showCount /></Form.Item>
      </Form></>}
    </Modal>
    {isAdmin && <RiskPolicyDrawer open={policyOpen} close={() => setPolicyOpen(false)} />}
  </>;
}
