import { useApplicationPlans } from '../plans/queries';
import { planOptions } from '../plans/options';
import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { App, Button, Card, Descriptions, Drawer, Input, Select, Space, Table } from 'antd';
import { ReloadOutlined, SearchOutlined, UndoOutlined } from '@ant-design/icons';
import { allApplications, api, queryString } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { BatchOverview } from '../../lib/management-types';
import { dateText, emptyTable, PageTitle, QueryError } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';
import { ChannelFilter } from '../commerce/ChannelFilter';
import { CopyValue } from '../../components/CopyValue';

export function BatchesPage() {
  const [page, setPage] = useState(1);
  const [applicationId, setApplicationId] = useState<string>();
  const [channelId, setChannelId] = useState<string>();
  const [planId, setPlanId] = useState<string>();
  const [createdBy, setCreatedBy] = useState('');
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState({ createdBy: '', search: '' });
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const { canManage } = useAuth();
  const { modal, message } = App.useApp();
  const cache = useQueryClient();
  const navigate = useNavigate();
  const apps = useQuery({ queryKey: ['applications', 'all'], queryFn: allApplications });
  const plans = useApplicationPlans(applicationId);
  const batches = useQuery({ queryKey: ['batches', page, applicationId, channelId, planId, filters], queryFn: () => api<Page<BatchOverview>>('/admin/batch-overview' + queryString({ page, pageSize: 20, applicationId, channelId, planId, ...filters })) });
  const detail = useQuery({ queryKey: ['batch', selectedId], enabled: !!selectedId, queryFn: () => api<BatchOverview>(`/admin/batch-overview/${selectedId}`) });
  function viewLicenses(id: string) { setSelectedId(null); navigate(`/licenses?batchId=${encodeURIComponent(id)}`); }
  function disable(batch: BatchOverview) {
    modal.confirm({ title: '禁用整个批次？', content: <><p>批次：<code className="break-word">{batch.id}</code></p><p>这会禁用本批次全部 {batch.quantity} 张卡密并撤销现有会话。</p></>, okText: '确认禁用批次', cancelText: '取消', okButtonProps: { danger: true }, onOk: async () => {
      try { await api(`/admin/license-batches/${batch.id}/disable`, { method: 'POST' }); await cache.invalidateQueries(); message.success('批次已禁用'); } catch (caught) { message.error((caught as Error).message); throw caught; }
    } });
  }
  const filter = () => { setFilters({ createdBy: createdBy.trim(), search: search.trim() }); setPage(1); };
  return <><PageTitle title="批次管理" subtitle="追踪每次签发的数量、使用情况与备注，按批次管理授权。" /><QueryError error={batches.error} retry={batches.refetch} /><QueryError error={apps.error} retry={apps.refetch} />
    <Card className="data-panel"><div className="panel-heading"><div><h2 className="panel-title">发行批次 <span className="count-badge">{batches.data?.total ?? '—'}</span></h2><p className="panel-description">已激活与已禁用数量可能重叠；批次不提供卡密明文。</p></div><Button icon={<ReloadOutlined />} onClick={() => batches.refetch()}>刷新</Button></div>
      <div className="filter-bar management-filter-bar">
        <div className="filter-field"><label htmlFor="batch-application-filter">应用</label><Select id="batch-application-filter" showSearch={{ optionFilterProp: 'label' }} allowClear placeholder="全部应用" value={applicationId} options={apps.data?.map(app => ({ value: app.id, label: app.name }))} onChange={value => { setApplicationId(value); setPlanId(undefined); setPage(1); }} /></div>
        <div className="filter-field"><label htmlFor="batch-plan-filter">卡种</label><Select id="batch-plan-filter" showSearch={{ optionFilterProp: 'label' }} allowClear disabled={!applicationId} loading={plans.isLoading} placeholder={applicationId ? '全部卡种' : '先选择应用'} value={planId} options={planOptions(plans.data, applicationId, 'history')} onChange={value => { setPlanId(value); setPage(1); }} /></div>
        <ChannelFilter id="batch-channel-filter" value={channelId} onChange={value => { setChannelId(value); setPage(1); }} />
        <div className="filter-field"><label htmlFor="batch-creator-filter">签发人 ID</label><Input id="batch-creator-filter" value={createdBy} onChange={event => setCreatedBy(event.target.value)} onPressEnter={filter} allowClear /></div>
        <div className="filter-field"><label htmlFor="batch-note-filter">批次备注</label><Input id="batch-note-filter" value={search} onChange={event => setSearch(event.target.value)} onPressEnter={filter} allowClear /></div>
        <div className="filter-actions"><Button icon={<SearchOutlined />} onClick={filter}>筛选</Button><Button icon={<UndoOutlined />} onClick={() => { setApplicationId(undefined); setChannelId(undefined); setPlanId(undefined); setCreatedBy(''); setSearch(''); setFilters({ createdBy: '', search: '' }); setPage(1); }}>重置</Button></div>
      </div><QueryError error={plans.error} retry={plans.refetch} />
      <Table<BatchOverview> rowKey="id" loading={batches.isLoading} dataSource={batches.data?.items} locale={{ emptyText: emptyTable }} scroll={{ x: 1400 }} pagination={{ current: page, pageSize: 20, total: batches.data?.total, onChange: setPage, showSizeChanger: false, showTotal: total => `共 ${total} 个批次` }} columns={[
        { title: '批次 ID', dataIndex: 'id', width: 180, ellipsis: true, render: value => <CopyValue value={value} label="批次 ID" compact /> },
        { title: '应用 / 卡种', width: 200, render: (_, batch) => <><strong>{batch.applicationName}</strong><div className="muted">{batch.planName}</div></> },
        { title: '数量', dataIndex: 'quantity', width: 70 }, { title: '已激活', dataIndex: 'activatedCount', width: 75 }, { title: '已禁用', dataIndex: 'disabledCount', width: 75 },
        { title: '签发人', dataIndex: 'createdByName', width: 130 }, { title: '签发时间', dataIndex: 'createdAt', width: 185, render: dateText },
        { title: '备注', dataIndex: 'note', ellipsis: true, render: value => value || '—' },
        { title: '操作', width: 225, render: (_, batch) => <Space size={0}><Button type="link" onClick={() => setSelectedId(batch.id)}>详情</Button><Button type="link" onClick={() => viewLicenses(batch.id)}>查看卡密</Button>{canManage && <Button type="link" danger onClick={() => disable(batch)}>禁用批次</Button>}</Space> },
      ]} />
    </Card><Drawer title="批次详情" open={!!selectedId} onClose={() => setSelectedId(null)} closable={{ 'aria-label': '关闭' }} size={680} loading={detail.isLoading}>
      <QueryError error={detail.error} retry={detail.refetch} />{detail.data && <><div className="section-toolbar"><Button type="primary" onClick={() => viewLicenses(detail.data.id)}>查看卡密</Button>{canManage && <Button danger onClick={() => disable(detail.data)}>禁用批次</Button>}</div><Descriptions bordered column={1} items={[
        { key: 'id', label: '批次 ID', children: <CopyValue value={detail.data.id} label="批次 ID" /> }, { key: 'app', label: '所属应用', children: detail.data.applicationName }, { key: 'plan', label: '卡种', children: detail.data.planName },
        { key: 'quantity', label: '签发数量', children: detail.data.quantity }, { key: 'activated', label: '已激活', children: detail.data.activatedCount }, { key: 'disabled', label: '已禁用', children: detail.data.disabledCount },
        { key: 'creator', label: '签发人', children: detail.data.createdByName }, { key: 'creatorId', label: '签发人 ID', children: <CopyValue value={detail.data.createdBy} label="签发人 ID" /> }, { key: 'time', label: '签发时间', children: dateText(detail.data.createdAt) }, { key: 'note', label: '备注', children: <span className="break-word">{detail.data.note || '—'}</span> },
      ]} /><p className="form-note">已激活与已禁用是独立统计，可能包含同一张卡密。</p></>}
    </Drawer>
  </>;
}
