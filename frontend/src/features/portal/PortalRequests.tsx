import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button, Card, Table } from 'antd';
import type { PortalRequest } from '../../lib/commerce-types';
import type { Page } from '../../lib/types';
import { dateText, emptyTable, QueryError } from '../../components/common';
import { RequestStatus, requestTypeNames } from '../commerce/commerce-shared';
import { usePortal } from './PortalProvider';

export function PortalRequests() {
  const { request } = usePortal();
  const [page, setPage] = useState(1);
  const requests = useQuery({ queryKey: ['portal-requests', page], queryFn: () => request<Page<PortalRequest>>(`/requests?page=${page}&pageSize=20`) });
  return <><QueryError error={requests.error} retry={requests.refetch} /><Card className="data-panel"><div className="panel-heading"><div><h2 className="panel-title">我的申请</h2><p className="panel-description">提交后由商家处理，批准或拒绝原因会保留在这里。</p></div><Button onClick={() => requests.refetch()}>刷新进度</Button></div>
    <Table<PortalRequest> rowKey="id" dataSource={requests.data?.items} loading={requests.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 800 }} pagination={{ current: page, pageSize: 20, total: requests.data?.total, onChange: setPage, showSizeChanger: false }} columns={[
      { title: '申请内容', width: 260, render: (_, item) => <div className="commerce-object"><strong>{requestTypeNames[item.type] ?? item.type}</strong><small>{item.note || '未填写说明'}</small></div> },
      { title: '状态', dataIndex: 'status', width: 130, render: status => <RequestStatus status={status} /> },
      { title: '商家回复', dataIndex: 'reviewReason', render: value => value || '待商家处理' }, { title: '提交时间', dataIndex: 'createdAt', width: 175, render: dateText },
      { title: '处理时间', dataIndex: 'reviewedAt', width: 175, render: dateText },
    ]} />
  </Card></>;
}
