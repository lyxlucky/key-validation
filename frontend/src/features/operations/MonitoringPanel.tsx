import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Alert, App, Button, Card, Space, Table, Tag } from 'antd';
import { DownloadOutlined, ReloadOutlined } from '@ant-design/icons';
import { api } from '../../lib/api';
import { downloadText } from '../../lib/download';
import type { Monitoring, MonitoringIncident, MonitoringRoute, MonitoringSample, WorkerError } from '../../lib/reliability-types';
import { dateText, emptyTable, QueryError } from '../../components/common';

const percent = (value: number | string | null) => value == null ? '无样本' : `${(Number(value) * 100).toFixed(1)}%`;
const latency = (value: number | string | null) => value == null ? '无样本' : `${Number(value).toFixed(1)} ms`;

export function MonitoringPanel() {
  const [sampling, setSampling] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const { message } = App.useApp();
  const monitoring = useQuery({ queryKey: ['monitoring'], queryFn: () => api<Monitoring>('/admin/monitoring'), refetchInterval: 60000 });
  const latest = monitoring.data?.latest;
  async function sample() {
    setSampling(true);
    try {
      const result = await api<MonitoringSample>('/admin/monitoring/sample', { method: 'POST' });
      await monitoring.refetch();
      if (result.databaseAvailable) message.success('监控采样已完成'); else message.warning('采样完成，数据库当前不可用');
    } catch (caught) { message.error((caught as Error).message); }
    finally { setSampling(false); }
  }
  async function exportMetrics() {
    setDownloading(true);
    try { downloadText(await api<string>('/admin/monitoring/metrics', { responseType: 'text' }), 'keygate-metrics.txt'); }
    catch (caught) { message.error((caught as Error).message); }
    finally { setDownloading(false); }
  }
  return <><div className="section-toolbar"><div className="muted">每 60 秒采样，最后更新 {dateText(monitoring.data?.generatedAt)}</div><Space wrap><Button icon={<DownloadOutlined aria-hidden />} loading={downloading} onClick={exportMetrics}>导出指标</Button><Button type="primary" icon={<ReloadOutlined aria-hidden />} loading={sampling} onClick={sample}>立即采样</Button></Space></div>
    <QueryError error={monitoring.error} retry={monitoring.refetch} />
    <div className="operations-metrics">
      <section className="operations-metric"><span>数据库</span><strong data-testid="database-status">{monitoring.data ? monitoring.data.databaseAvailable ? '可用' : '不可用' : '未知'}</strong><p>最近一次可用性检查</p></section>
      <section className="operations-metric"><span>验证请求</span><strong>{latest?.requestCount ?? '—'}</strong><p>本次采样窗口的实际流量</p></section>
      <section className="operations-metric"><span>服务端错误</span><strong>{latest?.serverErrorCount ?? '—'}</strong><p>返回 5xx 的请求数量</p></section>
      <section className="operations-metric"><span>错误率</span><strong>{latest ? percent(latest.errorRate) : '—'}</strong><p>没有流量时显示无样本</p></section>
      <section className="operations-metric"><span>P95 延迟</span><strong>{latest ? latency(latest.p95LatencyMs) : '—'}</strong><p>95% 请求低于此耗时</p></section>
    </div>
    <Alert className="operations-block" type="info" showIcon title="数据库不可用立即告警；流量达到 20 个样本后，错误率 ≥5% 或 P95 ≥500ms 连续三个窗口触发告警。" />
    {!!monitoring.data?.workerErrors.length && <Card className="data-panel operations-block" title="后台任务错误"><Table<WorkerError> rowKey={row => `${row.worker}:${row.occurredAt}`} dataSource={monitoring.data.workerErrors} pagination={false} scroll={{ x: 600 }} columns={[{ title: '任务', dataIndex: 'worker' }, { title: '错误分类', dataIndex: 'errorCode' }, { title: '发生时间', dataIndex: 'occurredAt', render: dateText }]} /></Card>}
    <Card className="data-panel operations-block" title="验证接口"><Table<MonitoringRoute> rowKey="route" dataSource={latest?.routes} locale={{ emptyText: emptyTable }} pagination={false} scroll={{ x: 700 }} columns={[
      { title: '接口', dataIndex: 'route', render: value => <code>{value}</code> }, { title: '请求数', dataIndex: 'requestCount' }, { title: '5xx', dataIndex: 'serverErrorCount' }, { title: '错误率', dataIndex: 'errorRate', render: percent }, { title: 'P95', dataIndex: 'p95LatencyMs', render: latency },
    ]} /></Card>
    <Card className="data-panel operations-block" title="服务事件"><Table<MonitoringIncident> rowKey="id" dataSource={monitoring.data?.incidents} locale={{ emptyText: emptyTable }} pagination={{ pageSize: 10, showSizeChanger: false }} scroll={{ x: 750 }} columns={[
      { title: '状态', render: (_, item) => <Tag color={item.resolvedAt ? 'success' : 'error'}>{item.resolvedAt ? '已恢复' : '处理中'}</Tag> }, { title: '事件', dataIndex: 'summary' }, { title: '开始时间', dataIndex: 'startedAt', render: dateText }, { title: '恢复时间', dataIndex: 'resolvedAt', render: dateText },
    ]} /></Card>
    <Card className="data-panel" title="历史采样"><Table<MonitoringSample> rowKey="id" dataSource={monitoring.data?.samples} locale={{ emptyText: emptyTable }} pagination={{ pageSize: 10, showSizeChanger: false }} scroll={{ x: 850 }} columns={[
      { title: '采样时间', dataIndex: 'sampledAt', render: dateText }, { title: '数据库', dataIndex: 'databaseAvailable', render: value => value ? '可用' : '不可用' }, { title: '验证请求', dataIndex: 'requestCount' }, { title: '5xx', dataIndex: 'serverErrorCount' }, { title: '错误率', dataIndex: 'errorRate', render: percent }, { title: 'P95', dataIndex: 'p95LatencyMs', render: latency },
    ]} /></Card>
  </>;
}
