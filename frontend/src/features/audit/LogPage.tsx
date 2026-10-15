import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button, Card, DatePicker, Grid, Input, Select, Table } from 'antd';
import { ReloadOutlined, SearchOutlined, UndoOutlined } from '@ant-design/icons';
import type { Dayjs } from 'dayjs';
import { api, queryString } from '../../lib/api';
import type { Audit, Page } from '../../lib/types';
import { actions, dateText, emptyTable, PageTitle, QueryError } from '../../components/common';
import { actorText, auditSummary, loginActions, methodText, targetText, targetTypes } from './audit-display';
import { AuditDetail, AuditResult } from './AuditDetail';

export function LogPage({ category = 'operation' }: { category?: 'operation' | 'login' }) {
  const login = category === 'login';
  const screens = Grid.useBreakpoint();
  const [page, setPage] = useState(1);
  const [action, setAction] = useState<string>();
  const [actor, setActor] = useState('');
  const [actorFilter, setActorFilter] = useState('');
  const [target, setTarget] = useState('');
  const [targetFilter, setTargetFilter] = useState('');
  const [dates, setDates] = useState<{ from?: string; to?: string }>({});
  const [dateRange, setDateRange] = useState<[Dayjs | null, Dayjs | null] | null>(null);
  const [selected, setSelected] = useState<Audit | null>(null);
  const logs = useQuery({ queryKey: ['audit', category, page, action, actorFilter, targetFilter, dates], queryFn: () => api<Page<Audit>>((login ? '/admin/login-logs' : '/admin/audit-logs') + queryString({ category, page, pageSize: 20, action, actorName: actorFilter || undefined, targetId: targetFilter || undefined, ...dates })) });
  const actionOptions = Object.entries(actions).filter(([code]) => loginActions.includes(code) === login).map(([value, label]) => ({ value, label }));
  return <><PageTitle title={login ? '登录日志' : '操作日志'} subtitle={login ? '查看账号登录、验证失败和退出记录，核查访问时间与来源。' : '看清谁对哪个对象做了什么，以及每次操作的结果和具体变化。'} />
    <QueryError error={logs.error} retry={logs.refetch} />
    <Card className="data-panel"><div className="panel-heading"><div><h2 className="panel-title">{login ? '登录记录' : '管理操作'} <span className="count-badge">{logs.data?.total ?? '—'}</span></h2><p className="panel-description">{login ? '历史记录缺少登录方式或来源时显示“未记录”。' : '对象名称与字段变化优先展示，追溯标识和原始记录可在详情中查看。'}</p></div><Button icon={<ReloadOutlined />} aria-label={login ? '刷新登录日志' : '刷新操作日志'} loading={logs.isFetching && !logs.isLoading} onClick={() => logs.refetch()}>刷新</Button></div>
      <div className="filter-bar management-filter-bar audit-filter-bar">
        <div className="filter-field"><label htmlFor={`${category}-action`}>{login ? '登录事件' : '操作类型'}</label><Select id={`${category}-action`} allowClear showSearch={{ optionFilterProp: 'label' }} placeholder={login ? '全部登录事件' : '全部操作'} value={action} options={actionOptions} onChange={value => { setAction(value); setPage(1); }} /></div>
        <div className="filter-field"><label htmlFor={`${category}-actor`}>{login ? '账号名称' : '操作人'}</label><Input.Search id={`${category}-actor`} placeholder="输入账号名称" value={actor} maxLength={128} onChange={event => setActor(event.target.value)} onSearch={value => { setActorFilter(value.trim()); setPage(1); }} enterButton={<Button aria-label="筛选操作人" icon={<SearchOutlined />} />} allowClear /></div>
        {!login && <div className="filter-field"><label htmlFor={`${category}-target`}>操作对象 ID</label><Input.Search id={`${category}-target`} placeholder="按对象 ID 精确定位" value={target} maxLength={36} onChange={event => setTarget(event.target.value)} onSearch={value => { setTargetFilter(value.trim()); setPage(1); }} enterButton={<Button aria-label="筛选操作对象" icon={<SearchOutlined />} />} allowClear /></div>}
        <div className="filter-field filter-field-dates"><label htmlFor={`${category}-date-from`}>日期范围</label><label className="filter-visually-hidden" htmlFor={`${category}-date-to`}>结束日期</label><DatePicker.RangePicker id={{ start: `${category}-date-from`, end: `${category}-date-to` }} classNames={{ popup: { root: 'audit-date-popup' } }} value={dateRange} onChange={values => { setDateRange(values); setDates({ from: values?.[0]?.startOf('day').toISOString(), to: values?.[1]?.add(1, 'day').startOf('day').toISOString() }); setPage(1); }} /></div>
        <div className="filter-actions"><Button icon={<UndoOutlined />} onClick={() => { setAction(undefined); setActor(''); setActorFilter(''); setTarget(''); setTargetFilter(''); setDates({}); setDateRange(null); setPage(1); }}>重置</Button></div>
      </div>
      <Table<Audit> rowKey="id" dataSource={logs.data?.items} loading={logs.isLoading} scroll={screens.md ? { x: login ? 1030 : 1170 } : undefined} locale={{ emptyText: emptyTable }} pagination={{ current: page, pageSize: 20, total: logs.data?.total, onChange: setPage, showSizeChanger: false, showTotal: total => `共 ${total} 条记录` }} columns={screens.md ? [
        { title: login ? '时间' : '操作时间', dataIndex: 'createdAt', width: 170, render: value => <span className="audit-time">{dateText(value)}</span> },
        { title: login ? '账号' : '操作人', width: 155, render: (_, log) => <strong className="audit-human-name">{actorText(log)}</strong> },
        ...(login ? [
          { title: '登录方式', width: 140, render: (_: unknown, log: Audit) => methodText(log) },
          { title: '登录结果', width: 90, render: (_: unknown, log: Audit) => <AuditResult result={log.result} /> },
          { title: '来源 IP', dataIndex: 'sourceIp', width: 150, render: (value: string | null) => value || '未记录' },
          { title: '说明', render: (_: unknown, log: Audit) => <div className="audit-human-summary"><strong>{actions[log.action] ?? '登录事件'}</strong><p>{auditSummary(log)}</p></div> },
        ] : [
          { title: '操作内容', width: 350, render: (_: unknown, log: Audit) => <div className="audit-human-summary"><strong>{actions[log.action] ?? '其他管理操作'}</strong><p>{auditSummary(log)}</p></div> },
          { title: '操作对象', width: 250, render: (_: unknown, log: Audit) => <div className="audit-human-summary"><strong>{targetText(log)}</strong><p>{targetTypes[log.targetType ?? ''] ?? '对象'}{log.applicationName && ` · ${log.applicationName}`}</p></div> },
          { title: '结果', width: 85, render: (_: unknown, log: Audit) => <AuditResult result={log.result} /> },
        ]),
        { title: '详情', width: 95, render: (_, log) => <Button type="link" onClick={() => setSelected(log)}>查看详情</Button> },
      ] : [{ title: login ? '登录记录' : '操作记录', render: (_, log) => <div className="audit-human-summary">
        <div className="audit-mobile-meta"><strong>{actorText(log)}</strong><AuditResult result={log.result} /></div>
        <small className="audit-mobile-time">{dateText(log.createdAt)}</small>
        <p><strong>{actions[log.action] ?? (login ? '登录事件' : '管理操作')}</strong>{!login && <> · {targetText(log)}</>}</p>
        <p>{auditSummary(log)}</p>
        {login && <p>{methodText(log)} · {log.sourceIp || '来源未记录'}</p>}
        <Button type="link" className="audit-mobile-detail" onClick={() => setSelected(log)}>查看详情</Button>
      </div> }]} />
    </Card><AuditDetail log={selected} close={() => setSelected(null)} />
  </>;
}
