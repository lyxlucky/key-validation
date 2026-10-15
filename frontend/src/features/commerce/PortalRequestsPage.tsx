import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button, Card, Select, Table } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { useSearchParams } from 'react-router-dom';
import { api, queryString } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { Channel, OrderCatalog, PortalRequest } from '../../lib/commerce-types';
import { dateText, emptyTable, PageTitle, QueryError } from '../../components/common';
import { useOperationsCatalog } from '../operations/useOperationsCatalog';
import { useAuth } from '../auth/AuthProvider';
import { PortalRequestReview } from './PortalRequestReview';
import { allPages, RequestStatus, requestStatusNames, requestTypeNames } from './commerce-shared';
import './commerce.css';

export function PortalRequestsPage() {
  const [params] = useSearchParams();
  const linkedCustomer = params.get('customerId') ?? undefined;
  const [filters, setFilters] = useState<{ page: number; status?: string; applicationId?: string; customerId?: string; channelId?: string; linkedCustomer?: string }>({ page: 1, customerId: linkedCustomer, linkedCustomer });
  if (filters.linkedCustomer !== linkedCustomer) setFilters(current => ({ ...current, customerId: linkedCustomer, linkedCustomer, page: 1 }));
  const [selected, setSelected] = useState<PortalRequest | null>(null);
  const auth = useAuth();
  const catalog = useOperationsCatalog();
  const channels = useQuery({ queryKey: ['channels', 'all'], queryFn: () => allPages<Channel>('/admin/channels'), enabled: auth.isAdmin });
  const customers = useQuery({ queryKey: ['order-catalog', undefined], queryFn: () => api<OrderCatalog>('/admin/orders/catalog') });
  const requests = useQuery({ queryKey: ['portal-admin-requests', filters], queryFn: () => api<Page<PortalRequest>>('/admin/portal-requests' + queryString({ page: filters.page, pageSize: 20, status: filters.status, applicationId: filters.applicationId, customerId: filters.customerId, channelId: filters.channelId })) });
  const customerName = (id: string) => customers.data?.customers.find(item => item.id === id)?.name || '客户名称未记录';
  return <><PageTitle title="客户申请" subtitle="核对客户续期和换绑需求，保留审批原因与处理结果。" />
    <QueryError error={requests.error} retry={requests.refetch} /><QueryError error={catalog.error} retry={catalog.refetch} /><QueryError error={customers.error} retry={customers.refetch} /><QueryError error={channels.error} retry={channels.refetch} />
    <Card className="data-panel"><div className="panel-heading"><div><h2 className="panel-title">续期与换绑申请 <span className="count-badge">{requests.data?.total ?? '—'}</span></h2><p className="panel-description">批准续期时生成订单，批准换绑时按现有规则释放指定设备。</p></div><Button icon={<ReloadOutlined aria-hidden />} onClick={() => requests.refetch()}>刷新申请</Button></div>
      <div className="filter-bar operations-filter"><div className="filter-field"><label htmlFor="portal-request-status">处理状态</label><Select id="portal-request-status" allowClear value={filters.status} placeholder="全部状态" options={Object.entries(requestStatusNames).map(([value, label]) => ({ value, label }))} onChange={status => setFilters(current => ({ ...current, status, page: 1 }))} /></div>
        <div className="filter-field"><label htmlFor="portal-request-app">所属应用</label><Select id="portal-request-app" allowClear showSearch={{ optionFilterProp: 'label' }} value={filters.applicationId} placeholder="全部应用" options={catalog.data?.applications.map(item => ({ value: item.id, label: item.name }))} onChange={applicationId => setFilters(current => ({ ...current, applicationId, customerId: undefined, page: 1 }))} /></div>
        <div className="filter-field"><label htmlFor="portal-request-customer">申请客户</label><Select id="portal-request-customer" allowClear showSearch={{ optionFilterProp: 'label' }} value={filters.customerId} placeholder="全部客户" options={customers.data?.customers.filter(item => !filters.applicationId || item.applicationId === filters.applicationId).map(item => ({ value: item.id, label: item.name || '已匿名化客户' }))} onChange={customerId => setFilters(current => ({ ...current, customerId, page: 1 }))} /></div>
        {auth.isAdmin && <div className="filter-field"><label htmlFor="portal-request-channel">所属渠道</label><Select id="portal-request-channel" allowClear showSearch={{ optionFilterProp: 'label' }} value={filters.channelId} placeholder="全部渠道" options={channels.data?.map(item => ({ value: item.id, label: item.name }))} onChange={channelId => setFilters(current => ({ ...current, channelId, page: 1 }))} /></div>}
      </div>
      <Table<PortalRequest> rowKey="id" loading={requests.isLoading} dataSource={requests.data?.items} locale={{ emptyText: emptyTable }} scroll={{ x: 1050 }} pagination={{ current: filters.page, pageSize: 20, total: requests.data?.total, onChange: page => setFilters(current => ({ ...current, page })), showSizeChanger: false }} columns={[
        { title: '客户', dataIndex: 'customerId', width: 180, render: id => <strong>{customerName(id)}</strong> },
        { title: '申请内容', width: 270, render: (_, request) => <div className="commerce-object"><strong>{requestTypeNames[request.type] ?? request.type}</strong><small>{request.note || '未填写说明'}</small></div> },
        { title: '状态', dataIndex: 'status', width: 130, render: status => <RequestStatus status={status} /> },
        { title: '审批原因', dataIndex: 'reviewReason', width: 190, render: value => value || '等待处理' }, { title: '提交时间', dataIndex: 'createdAt', width: 175, render: dateText },
        { title: '操作', fixed: 'right', width: 110, render: (_, request) => <Button type="link" onClick={() => setSelected(request)}>{request.status === 'pending' ? '审批' : '查看结果'}</Button> },
      ]} />
    </Card><PortalRequestReview request={selected} customerName={selected ? customerName(selected.customerId) : ''} catalog={catalog.data} close={() => setSelected(null)} />
  </>;
}
