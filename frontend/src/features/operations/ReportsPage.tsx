import { usePlanCatalog } from '../plans/queries';
import { planOptions } from '../plans/options';
import { useLayoutEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Alert, App, Button, Card, Input, Select, Table, Tabs } from 'antd';
import { ReloadOutlined, SearchOutlined } from '@ant-design/icons';
import { api, queryString } from '../../lib/api';
import type { OperationsBreakdown, OperationsCounts, OperationsDay, OperationsReport } from '../../lib/operations-types';
import { dateText, emptyTable, PageTitle, QueryError } from '../../components/common';
import { ChannelFilter } from '../commerce/ChannelFilter';

function dayOffset(offset: number) { const value = new Date(); value.setUTCDate(value.getUTCDate() + offset); return value.toISOString().slice(0, 10); }
function throughDay(value: string) { const date = new Date(`${value}T00:00:00Z`); date.setUTCDate(date.getUTCDate() + 1); return date.toISOString(); }
const metrics: [keyof OperationsCounts, string, string][] = [
  ['issued', '签发卡密', '期间新发行的授权'], ['activated', '首次激活', '按授权去重，重复激活不增加'],
  ['expired', '已到期', '当前到期时间落在查询区间'], ['onlineLicenses', '在线授权', '当前有效在线的去重授权'],
  ['onlineSessions', '在线会话', '同时满足租约和绝对有效期'],
];

function DailyChart({ days }: { days: OperationsDay[] }) {
  const chart = useRef<SVGSVGElement>(null);
  const [chartWidth, setChartWidth] = useState(960);
  useLayoutEffect(() => {
    const element = chart.current;
    if (!element) return;
    const resize = () => setChartWidth(Math.max(240, element.clientWidth));
    resize();
    const observer = new ResizeObserver(resize);
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  const maximum = Math.max(1, ...days.map(day => Math.max(Number(day.issued), Number(day.activated))));
  const width = chartWidth - 60; const height = 180; const step = width / Math.max(1, days.length);
  return <div className="operations-chart-container">
    <div className="operations-chart-legend"><span><i className="issued" />签发</span><span><i className="activated" />首次激活</span></div>
    <svg ref={chart} className="operations-chart" viewBox={`0 0 ${chartWidth} 230`} role="img" aria-label="每日签发与首次激活数量，详细数字见下方表格">
      <text x="2" y="24" fontSize="12" fill="#586579">{maximum}</text>
      <line x1="40" y1="200" x2={chartWidth - 20} y2="200" stroke="#e4e9f1" />
      {days.map((day, index) => <g key={day.date}>
        <title>{day.date}：签发 {day.issued}，首次激活 {day.activated}</title>
        <rect x={40 + index * step + step * 0.12} y={200 - Number(day.issued) / maximum * height} width={step * 0.32} height={Number(day.issued) / maximum * height} rx="1" fill="#2563eb" />
        <rect x={40 + index * step + step * 0.52} y={200 - Number(day.activated) / maximum * height} width={step * 0.32} height={Number(day.activated) / maximum * height} rx="1" fill="#16a34a" />
      </g>)}
      <text x="40" y="222" fontSize="12" fill="#586579">{days[0]?.date}</text>
      <text x={chartWidth - 20} y="222" textAnchor="end" fontSize="12" fill="#586579">{days.at(-1)?.date}</text>
    </svg>
  </div>;
}

export function ReportsPage() {
  const [applicationId, setApplicationId] = useState<string>();
  const [channelId, setChannelId] = useState<string>();
  const [planId, setPlanId] = useState<string>();
  const [start, setStart] = useState(() => dayOffset(-29));
  const [end, setEnd] = useState(() => dayOffset(0));
  const [range, setRange] = useState(() => ({ from: `${dayOffset(-29)}T00:00:00Z`, to: throughDay(dayOffset(0)) }));
  const { message } = App.useApp();
  const catalog = usePlanCatalog('reports');
  const report = useQuery({ queryKey: ['operations-report', applicationId, channelId, planId, range], queryFn: () => api<OperationsReport>('/admin/reports/operations' + queryString({ applicationId, channelId, planId, ...range })), refetchInterval: 60000 });
  function applyRange() {
    const from = new Date(`${start}T00:00:00Z`); const last = new Date(`${end}T00:00:00Z`);
    const duration = (last.getTime() - from.getTime()) / 86400000 + 1;
    if (!Number.isFinite(duration) || duration < 1 || duration > 366) { message.error('请选择不超过 366 天的有效日期区间'); return; }
    setRange({ from: from.toISOString(), to: throughDay(end) });
  }
  return <><PageTitle title="运营统计" subtitle="按应用、卡种和日期查看签发、激活、到期与当前在线情况。" extra={<Button icon={<ReloadOutlined />} loading={report.isFetching} onClick={() => report.refetch()}>刷新统计</Button>} />
    <QueryError error={catalog.error} retry={catalog.refetch} /><QueryError error={report.error} retry={report.refetch} />
    <Card className="operations-block"><div className="operations-filter">
      <div className="filter-field"><label htmlFor="report-application">统计应用</label><Select id="report-application" allowClear showSearch={{ optionFilterProp: 'label' }} value={applicationId} placeholder="全部可见应用" options={catalog.data?.applications.map(app => ({ value: app.id, label: app.name }))} onChange={value => { setApplicationId(value); setPlanId(undefined); }} /></div>
      <div className="filter-field"><label htmlFor="report-plan">统计卡种</label><Select id="report-plan" allowClear showSearch={{ optionFilterProp: 'label' }} value={planId} disabled={!applicationId} placeholder="全部卡种（含试用）" options={planOptions(catalog.data?.plans, applicationId, 'history')} onChange={setPlanId} /></div>
      <ChannelFilter id="report-channel-filter" value={channelId} onChange={setChannelId} />
      <div className="filter-field"><label htmlFor="report-from">统计起始日（UTC）</label><Input id="report-from" type="date" value={start} onChange={event => setStart(event.target.value)} /></div>
      <div className="filter-field"><label htmlFor="report-to">统计结束日（UTC）</label><Input id="report-to" type="date" value={end} onChange={event => setEnd(event.target.value)} /></div>
      <Button icon={<SearchOutlined />} onClick={applyRange}>查询区间</Button>
    </div><div className="operations-subtitle"><span>统计时区：UTC</span><span>包含结束日，每 60 秒刷新</span><span>生成于 {dateText(report.data?.generatedAt)}</span></div></Card>
    <div className="operations-metrics">{metrics.map(([field, label, hint]) => <section key={field} className="operations-metric" aria-label={label}><span>{label}</span><strong data-testid={`report-${field}`}>{report.data?.totals[field] ?? '—'}</strong><p>{hint}</p></section>)}</div>
    <Alert className="operations-block" type="info" showIcon title="到期数字按授权当前的到期时间统计；续期后会随之变化。在线数字是当前快照，不随所选日期区间累加。" />
    <Card className="data-panel operations-block"><div className="panel-heading"><div><h2 className="panel-title">每日趋势</h2><p className="panel-description">签发和首次激活按 UTC 自然日汇总。</p></div></div><DailyChart days={report.data?.daily ?? []} /></Card>
    <Card className="data-panel"><div className="operations-content"><Tabs items={[
      { key: 'breakdown', label: '应用与卡种', children: <Table<OperationsBreakdown> rowKey={row => `${row.applicationId}:${row.planId ?? 'trial'}`} dataSource={report.data?.breakdown} loading={report.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 900 }} pagination={{ pageSize: 20, showSizeChanger: false }} columns={[
        { title: '应用', dataIndex: 'applicationName' }, { title: '卡种', dataIndex: 'planName', render: value => value ?? '试用授权' }, ...metrics.map(([field, label]) => ({ title: label, dataIndex: field })),
      ]} /> },
      { key: 'daily', label: '每日明细', children: <Table<OperationsDay> rowKey="date" dataSource={report.data?.daily} locale={{ emptyText: emptyTable }} scroll={{ x: 550 }} pagination={{ pageSize: 15, showSizeChanger: false }} columns={[
        { title: '日期（UTC）', dataIndex: 'date' }, { title: '签发卡密', dataIndex: 'issued' }, { title: '首次激活', dataIndex: 'activated' }, { title: '已到期', dataIndex: 'expired' },
      ]} /> },
    ]} /></div></Card>
  </>;
}
