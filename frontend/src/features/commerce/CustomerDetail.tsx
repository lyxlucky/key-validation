import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button, Descriptions, Drawer, Table, Tag } from 'antd';
import { useNavigate } from 'react-router-dom';
import { api } from '../../lib/api';
import type { Customer } from '../../lib/commerce-types';
import type { License, Page } from '../../lib/types';
import { dateText, emptyTable, LicenseStatus, QueryError } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';

export function CustomerDetail({ id, close }: { id: string | null; close: () => void }) {
  const [pagination, setPagination] = useState({ id, page: 1 });
  const page = pagination.id === id ? pagination.page : 1;
  const auth = useAuth();
  const navigate = useNavigate();
  const customer = useQuery({ queryKey: ['customer', id], queryFn: () => api<Customer>(`/admin/customers/${id}`), enabled: !!id });
  const licenses = useQuery({ queryKey: ['customer-licenses', id, page], queryFn: () => api<Page<License>>(`/admin/customers/${id}/licenses?page=${page}&pageSize=10`), enabled: !!id });
  const value = customer.data;
  return <Drawer title="客户详情" className="commerce-detail" open={!!id} loading={customer.isLoading} size={780} onClose={close} closable={{ 'aria-label': '关闭' }} destroyOnHidden>
    <QueryError error={customer.error} retry={customer.refetch} /><QueryError error={licenses.error} retry={licenses.refetch} />
    {value && <><Descriptions column={{ xs: 1, sm: 2 }} items={[
      { key: 'name', label: '客户', children: value.name || '已匿名化客户' },
      { key: 'state', label: '状态', children: value.anonymizedAt ? '已匿名化' : value.disabledAt ? '已停用' : '已启用' },
      { key: 'email', label: '邮箱', children: value.email || '未填写' }, { key: 'phone', label: '电话', children: value.phone || '未填写' },
      { key: 'access', label: '自助访问码', children: value.hasAccessCode ? '已生成，支持轮换' : '尚未生成' }, { key: 'created', label: '建档时间', children: dateText(value.createdAt) },
      { key: 'note', label: '备注', span: 2, children: value.note || '未填写' },
    ]} />
      {auth.hasPermission('orders.manage') && <div className="commerce-detail-actions"><Button onClick={() => { close(); navigate(`/orders?customerId=${id}`); }}>查看客户订单</Button><Button onClick={() => { close(); navigate(`/portal-requests?customerId=${id}`); }}>查看续期与换绑申请</Button></div>}
    </>}
    <section className="commerce-detail-section"><h3>关联授权</h3><Table<License> rowKey="id" loading={licenses.isLoading} dataSource={licenses.data?.items} locale={{ emptyText: emptyTable }} scroll={{ x: 650 }} pagination={{ current: page, pageSize: 10, total: licenses.data?.total, onChange: page => setPagination({ id, page }), showSizeChanger: false }} columns={[
      { title: '卡密提示', dataIndex: 'keyHint', width: 170 }, { title: '状态', dataIndex: 'status', render: status => <LicenseStatus status={status} /> },
      { title: '到期时间', dataIndex: 'expiresAt', width: 180, render: dateText }, { title: '功能', dataIndex: 'features', render: (values: string[]) => values.length ? values.map(value => <Tag key={value}>{value}</Tag>) : '无附加功能' },
    ]} /></section>
  </Drawer>;
}
