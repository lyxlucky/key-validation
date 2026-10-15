import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Alert, Button, Card, Segmented, Select, Table, Tag } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { allApplications, api, queryString } from '../../lib/api';
import type { ExpiringLicenses } from '../../lib/management-types';
import type { License } from '../../lib/types';
import { dateText, emptyTable, PageTitle, QueryError } from '../../components/common';
import { LicenseDrawer } from '../licenses/LicenseDrawer';
import { ChannelFilter } from '../commerce/ChannelFilter';

export function ExpiryPage() {
  const [withinDays, setWithinDays] = useState(7);
  const [applicationId, setApplicationId] = useState<string>();
  const [channelId, setChannelId] = useState<string>();
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const apps = useQuery({ queryKey: ['applications', 'all'], queryFn: allApplications });
  const expiring = useQuery({ queryKey: ['expiring', withinDays, applicationId, channelId, page], queryFn: () => api<ExpiringLicenses>('/admin/expiring-licenses' + queryString({ withinDays, applicationId, channelId, page, pageSize: 20 })) });
  useEffect(() => {
    if (expiring.data) setPage(current => Math.min(current, Math.max(1, Math.ceil(expiring.data.total / 20))));
  }, [expiring.data]);
  return <><PageTitle title="到期提醒" subtitle="按到期时间查看近期需要跟进的授权，在详情中办理续期。" /><QueryError error={expiring.error} retry={expiring.refetch} /><QueryError error={apps.error} retry={apps.refetch} />
    <Card className="data-panel"><div className="panel-heading"><div><h2 className="panel-title">即将到期的卡密 <span className="count-badge">{expiring.data?.total ?? '—'}</span></h2><p className="panel-description">仅包含已激活、未禁用且所属应用启用的卡密。</p></div><Button icon={<ReloadOutlined />} onClick={() => expiring.refetch()}>刷新</Button></div>
      <div className="filter-bar management-filter-bar"><div className="filter-field"><label id="expiry-window-label">到期窗口</label><Segmented aria-labelledby="expiry-window-label" value={withinDays} options={[{ value: 3, label: '3 天内' }, { value: 7, label: '7 天内' }, { value: 30, label: '30 天内' }]} onChange={value => { setWithinDays(Number(value)); setPage(1); }} /></div><div className="filter-field"><label htmlFor="expiry-application-filter">应用</label><Select id="expiry-application-filter" showSearch={{ optionFilterProp: 'label' }} allowClear placeholder="全部应用" value={applicationId} options={apps.data?.map(app => ({ value: app.id, label: app.name }))} onChange={value => { setApplicationId(value); setPage(1); }} /></div><ChannelFilter id="expiry-channel-filter" value={channelId} onChange={value => { setChannelId(value); setPage(1); }} /></div>
      {expiring.data && <Alert type="info" showIcon className="query-error" title={`查询窗口：服务端当前时间起 ${expiring.data.withinDays} 天内（每天 24 小时）`} description={`服务端时间：${dateText(expiring.data.serverTime)}；按到期时间由近到远排列。`} />}
      <Table<License> rowKey="id" loading={expiring.isLoading} dataSource={expiring.data?.items} scroll={{ x: 980 }} locale={{ emptyText: emptyTable }} pagination={{ current: page, pageSize: 20, total: expiring.data?.total, onChange: setPage, showSizeChanger: false, showTotal: total => `共 ${total} 张卡密` }} columns={[
        { title: '到期时间', dataIndex: 'expiresAt', width: 185, render: dateText }, { title: '卡密标识', dataIndex: 'keyHint', width: 160, render: value => <code className="code-chip">{value}</code> },
        { title: '所属应用', dataIndex: 'applicationId', width: 180, render: value => apps.data?.find(app => app.id === value)?.name ?? '—' }, { title: '备注', dataIndex: 'note', ellipsis: true, render: value => value || '—' },
        { title: '标签', dataIndex: 'tags', width: 200, render: tags => <div className="license-tags">{(tags as string[] | undefined)?.map(tag => <Tag key={tag}>{tag}</Tag>)}</div> },
        { title: '操作', width: 80, fixed: 'right', render: (_, license) => <Button type="link" onClick={() => setSelectedId(license.id)}>详情</Button> },
      ]} />
    </Card><LicenseDrawer id={selectedId} close={() => setSelectedId(null)} />
  </>;
}
