import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Alert, App, Button, Card, Input, Select, Space, Table, Tag } from 'antd';
import { FilterOutlined, KeyOutlined, PlusOutlined, ReloadOutlined, SearchOutlined, UndoOutlined } from '@ant-design/icons';
import { Link, useSearchParams } from 'react-router-dom';
import { allApplications, api, queryString } from '../../lib/api';
import type { License, LicenseDetail, Page } from '../../lib/types';
import { dateText, durationText, emptyTable, LicenseStatus, PageTitle, QueryError, statusNames } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';
import { IssueModal } from './IssueModal';
import { LicenseDrawer } from './LicenseDrawer';
import { useLicenseOperations } from '../operations/LicenseOperations';
import { AdvancedLicenseFilters, advancedFilterQuery, emptyAdvancedFilters } from './AdvancedLicenseFilters';
import { ChannelFilter } from '../commerce/ChannelFilter';

export function LicensesPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const [issueOpen, setIssueOpen] = useState(searchParams.get('generate') === '1');
  const [applicationId, setApplicationId] = useState<string>();
  const [channelId, setChannelId] = useState<string>();
  const [status, setStatus] = useState<string>();
  const [batchId, setBatchId] = useState(searchParams.get('batchId') ?? '');
  const [batchFilter, setBatchFilter] = useState(searchParams.get('batchId') ?? '');
  const [note, setNote] = useState('');
  const [tag, setTag] = useState('');
  const [metadataFilter, setMetadataFilter] = useState({ note: '', tag: '' });
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const [advancedDraft, setAdvancedDraft] = useState(emptyAdvancedFilters);
  const [advancedFilter, setAdvancedFilter] = useState<Record<string, string>>({});
  const [selectedKeys, setSelectedKeys] = useState<string[]>([]);
  const [page, setPage] = useState(1);
  const [key, setKey] = useState('');
  const [searching, setSearching] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(searchParams.get('licenseId'));
  const { message } = App.useApp();
  const { canManage, canIssue, access, hasPermission } = useAuth();
  const { openOperation } = useLicenseOperations();
  const apps = useQuery({ queryKey: ['applications', 'all'], queryFn: allApplications });
  const licenses = useQuery({ queryKey: ['licenses', page, applicationId, channelId, status, batchFilter, metadataFilter, advancedFilter], queryFn: () => {
    const filters = { page, pageSize: 20, applicationId, channelId, status, batchId: batchFilter || undefined, ...metadataFilter };
    return Object.keys(advancedFilter).length
      ? api<Page<License>>('/admin/licenses/query', { method: 'POST', body: { ...filters, ...advancedFilter } })
      : api<Page<License>>('/admin/licenses' + queryString(filters));
  } });
  useEffect(() => {
    if (licenses.data) setPage(current => Math.min(current, Math.max(1, Math.ceil(licenses.data.total / 20))));
  }, [licenses.data]);
  const linkedBatch = searchParams.get('batchId') ?? '';
  const linkedLicense = searchParams.get('licenseId');
  const [linkedLocation, setLinkedLocation] = useState({ batch: linkedBatch, license: linkedLicense });
  if (linkedLocation.batch !== linkedBatch || linkedLocation.license !== linkedLicense) {
    setLinkedLocation({ batch: linkedBatch, license: linkedLicense });
    if (linkedLocation.license !== linkedLicense) setSelectedId(linkedLicense);
    if (linkedLocation.batch !== linkedBatch) { setBatchId(linkedBatch); setBatchFilter(linkedBatch); setPage(1); setSelectedKeys([]); }
  }
  const selectionSource = JSON.stringify([page, applicationId, channelId, status, batchFilter, metadataFilter, advancedFilter]);
  const previousSelectionSource = useRef(selectionSource);
  useEffect(() => {
    if (previousSelectionSource.current !== selectionSource) { previousSelectionSource.current = selectionSource; setSelectedKeys([]); }
  }, [selectionSource]);
  const selectedLicenses = (licenses.data?.items ?? []).filter(license => selectedKeys.includes(license.id));
  function applyFilters() {
    try {
      if (advancedDraft.deviceId.trim() && !applicationId) throw new Error('按设备查找时请先选择应用');
      const next = advancedFilterQuery(advancedDraft);
      setAdvancedFilter(next); setBatchFilter(batchId.trim()); setMetadataFilter({ note: note.trim(), tag: tag.trim() }); setPage(1); setSelectedKeys([]);
    } catch (error) { message.error((error as Error).message); }
  }
  async function searchKey() {
    if (!key.trim()) return;
    setSearching(true);
    try { const detail = await api<LicenseDetail>('/admin/licenses/search', { method: 'POST', body: { licenseKey: key.trim() } }); setSelectedId(detail.license!.id!); setKey(''); } catch (e) { message.error((e as Error).message); } finally { setSearching(false); }
  }
  return <><PageTitle title="卡密管理" subtitle="查看发行与激活状态，管理每张卡密的设备和会话。" extra={canIssue && <Button type="primary" icon={<PlusOutlined />} onClick={() => setIssueOpen(true)}>生成卡密</Button>} />
    {(access?.channelId || channelId) && <Alert className="query-error" type="info" showIcon title="渠道卡密需通过续期订单处理" action={hasPermission('orders.manage') ? <Link to="/orders">前往订单管理</Link> : undefined} />}
    <QueryError error={apps.error} retry={apps.refetch} /><QueryError error={licenses.error} retry={licenses.refetch} />
    <Card className="data-panel">
      <div className="panel-heading"><div><h2 className="panel-title">卡密列表 <span className="count-badge">{licenses.data?.total ?? '—'}</span></h2><p className="panel-description">集中查看授权状态，按应用或发行批次快速定位。</p></div><Button icon={<ReloadOutlined />} aria-label="刷新卡密" onClick={() => licenses.refetch()} loading={licenses.isFetching && !licenses.isLoading}>刷新</Button></div>
      <div className="filter-bar management-filter-bar">
        <div className="filter-field"><label htmlFor="license-application-filter">应用</label><Select id="license-application-filter" aria-label="筛选应用" allowClear showSearch={{ optionFilterProp: 'label' }} placeholder="全部应用" value={applicationId} options={apps.data?.map(x => ({ value: x.id, label: x.name }))} onChange={value => { setApplicationId(value); setBatchId(''); setBatchFilter(''); setAdvancedDraft(draft => ({ ...draft, deviceId: '' })); setAdvancedFilter(current => { const { deviceId: _device, ...rest } = current; return rest; }); setPage(1); }} /></div>
        <div className="filter-field filter-field-status"><label htmlFor="license-status-filter">卡密状态</label><Select id="license-status-filter" aria-label="筛选状态" allowClear placeholder="全部状态" value={status} options={Object.entries(statusNames).map(([value, label]) => ({ value, label }))} onChange={value => { setStatus(value); setPage(1); }} /></div>
        <ChannelFilter id="license-channel-filter" value={channelId} onChange={value => { setChannelId(value); setPage(1); }} />
        <div className="filter-field filter-field-batch"><label htmlFor="license-batch-filter">发行批次</label><Input id="license-batch-filter" placeholder="按批次 ID 筛选" value={batchId} onChange={e => { setBatchId(e.target.value); setSelectedKeys([]); }} onPressEnter={applyFilters} allowClear /></div>
        <div className="filter-field"><label htmlFor="license-note-filter">备注关键字</label><Input id="license-note-filter" value={note} onChange={event => { setNote(event.target.value); setSelectedKeys([]); }} onPressEnter={applyFilters} maxLength={500} allowClear /></div>
        <div className="filter-field"><label htmlFor="license-tag-filter">标签</label><Input id="license-tag-filter" value={tag} onChange={event => { setTag(event.target.value); setSelectedKeys([]); }} onPressEnter={applyFilters} placeholder="精确匹配单个标签" maxLength={32} allowClear /></div>
        <div className="filter-actions"><Button icon={<SearchOutlined aria-hidden />} onClick={applyFilters}>筛选</Button><Button icon={<FilterOutlined />} aria-label="高级筛选" aria-expanded={advancedOpen} onClick={() => setAdvancedOpen(open => !open)}>高级筛选{Object.keys(advancedFilter).length > 0 && <span className="filter-active-dot" />}</Button><Button icon={<UndoOutlined />} onClick={() => { setApplicationId(undefined); setChannelId(undefined); setStatus(undefined); setBatchId(''); setBatchFilter(''); setNote(''); setTag(''); setMetadataFilter({ note: '', tag: '' }); setAdvancedDraft(emptyAdvancedFilters); setAdvancedFilter({}); setSelectedKeys([]); setPage(1); setSearchParams({}); }}>重置</Button></div>
      </div>
      {advancedOpen && <AdvancedLicenseFilters value={advancedDraft} onChange={setAdvancedDraft} />}
      <div className="key-search management-key-search"><div className="key-search-label"><span className="key-search-icon"><KeyOutlined /></span><div><label htmlFor="license-key-search">完整卡密查找</label><span>列表仅显示脱敏标识</span></div></div><div className="key-search-controls"><Input id="license-key-search" aria-label="完整卡密" prefix={<SearchOutlined />} placeholder="输入完整卡密精确查找" value={key} onChange={e => setKey(e.target.value)} onPressEnter={searchKey} maxLength={256} autoComplete="off" allowClear /><Button onClick={searchKey} loading={searching}>查找</Button></div></div>
    {canManage && <div className="license-bulk-toolbar"><div><strong>已选 {selectedLicenses.length} 张</strong><span className="muted">仅选择当前页，最多 100 张</span></div><Space wrap>{!access?.channelId && !channelId && <Button disabled={!selectedLicenses.length} onClick={() => openOperation('extend', selectedLicenses)}>批量续期</Button>}<Button disabled={!selectedLicenses.length} onClick={() => openOperation('metadata', selectedLicenses)}>批量编辑备注标签</Button><Button danger disabled={!selectedLicenses.length} onClick={() => openOperation('disable', selectedLicenses)}>批量禁用</Button><Button disabled={!selectedLicenses.length} onClick={() => openOperation('enable', selectedLicenses)}>批量恢复</Button><Button type="text" disabled={!selectedLicenses.length} onClick={() => setSelectedKeys([])}>清除选择</Button></Space></div>}
    <Table<License> rowKey="id" rowSelection={canManage ? { selectedRowKeys: selectedKeys, preserveSelectedRowKeys: false, onChange: keys => setSelectedKeys(keys.map(String).slice(0, 100)) } : undefined} dataSource={licenses.data?.items} loading={licenses.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 1400 }} pagination={{ current: page, pageSize: 20, total: licenses.data?.total, onChange: setPage, showSizeChanger: false, showTotal: total => `共 ${total} 张卡密` }} columns={[
      { title: '卡密标识', dataIndex: 'keyHint', width: 190, render: value => <code className="code-chip">{value}</code> },
      { title: '状态', dataIndex: 'status', width: 110, render: value => <LicenseStatus status={value} /> },
      { title: '所属应用', dataIndex: 'applicationId', render: value => apps.data?.find(x => x.id === value)?.name ?? '—' },
      { title: '授权时长', dataIndex: 'durationSeconds', render: durationText },
      { title: '设备 / 在线上限', render: (_, license) => `${license.maxDevices} 台 / ${license.maxConcurrentSessions} 个` },
      { title: '最晚激活时间', dataIndex: 'activateBefore', render: value => value ? dateText(value) : '不限' },
      { title: '到期时间', dataIndex: 'expiresAt', render: dateText },
      { title: '创建时间', dataIndex: 'createdAt', render: dateText },
      { title: '备注', dataIndex: 'note', width: 160, ellipsis: true, render: value => value || '—' },
      { title: '标签', dataIndex: 'tags', width: 180, render: tags => <div className="license-tags">{(tags as string[] | undefined)?.map(value => <Tag key={value}>{value}</Tag>)}</div> },
      { title: '操作', width: 80, fixed: 'right', render: (_, license) => <Button type="link" onClick={() => setSelectedId(license.id)}>详情</Button> },
    ]} /></Card>
    {canIssue && <IssueModal open={issueOpen} close={() => { setIssueOpen(false); const next = new URLSearchParams(searchParams); next.delete('generate'); setSearchParams(next); }} applications={apps.data ?? []} applicationsLoading={apps.isLoading} applicationsError={apps.error} retryApplications={apps.refetch} />}<LicenseDrawer id={selectedId} close={() => { setSelectedId(null); if (linkedLicense) { const next = new URLSearchParams(searchParams); next.delete('licenseId'); setSearchParams(next, { replace: true }); } }} />
  </>;
}
