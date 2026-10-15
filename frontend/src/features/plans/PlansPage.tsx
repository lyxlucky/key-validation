import { useState } from 'react';
import { Button, Card, Input, Select, Table, Tag } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { PageTitle, QueryError, durationText, emptyTable } from '../../components/common';
import { useGlobalPlans } from './queries';
import { PlanEditor } from './PlanEditor';
import { PlanApplicationsDrawer } from './PlanApplicationsDrawer';
import type { GlobalPlan } from './types';

export function PlansPage() {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<'enabled' | 'disabled'>();
  const [editing, setEditing] = useState<GlobalPlan | 'new' | null>(null);
  const [viewing, setViewing] = useState<GlobalPlan | null>(null);
  const plans = useGlobalPlans(page, search,
    status === undefined ? undefined : status === 'enabled');
  return <>
    <PageTitle title="卡种管理" subtitle="统一配置授权规则，供多个应用引用。"
      extra={<Button type="primary" icon={<PlusOutlined aria-hidden />}
        onClick={() => setEditing('new')}>创建卡种</Button>} />
    <Card className="data-panel">
      <div className="filter-bar plan-filter-bar">
        <Input.Search aria-label="搜索卡种" placeholder="搜索卡种名称" allowClear
          onSearch={value => { setSearch(value.trim()); setPage(1); }} />
        <Select aria-label="卡种发行状态" allowClear placeholder="全部发行状态"
          value={status}
          options={[{ value: 'enabled', label: '可发行' }, { value: 'disabled', label: '已停发' }]}
          onChange={value => { setStatus(value); setPage(1); }} />
        <Button onClick={() => plans.refetch()}>刷新</Button>
      </div>
      <QueryError error={plans.error} retry={plans.refetch} />
      <Table<GlobalPlan> rowKey="id" dataSource={plans.data?.items}
        loading={plans.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 960 }}
        pagination={{ current: page, pageSize: 20, total: plans.data?.total,
          showSizeChanger: false, onChange: setPage }} columns={[
          { title: '卡种名称', dataIndex: 'name', render: (name, row) =>
            <><strong>{name}</strong><div className="muted">{row.id.slice(0, 8)}</div></> },
          { title: '授权时长', dataIndex: 'durationSeconds', render: durationText },
          { title: '设备上限', dataIndex: 'maxDevices' },
          { title: '在线上限', dataIndex: 'maxConcurrentSessions' },
          { title: '激活期限', dataIndex: 'activationValidityDays',
            render: value => value == null ? '不限' : String(value) + ' 天' },
          { title: '发行状态', dataIndex: 'isEnabled', render: value =>
            <Tag color={value ? 'success' : 'default'}>{value ? '可发行' : '已停发'}</Tag> },
          { title: '关联应用', render: (_, row) =>
            <Button type="link" onClick={() => setViewing(row)}>
              {String(row.referenceCount) + ' 个应用'}
            </Button> },
          { title: '操作', fixed: 'right', width: 80, render: (_, row) =>
            <Button type="link" disabled={!!plans.error}
              onClick={() => setEditing(row)}>编辑</Button> },
        ]} />
    </Card>
    <PlanEditor open={editing !== null} plan={editing === 'new' ? null : editing}
      close={() => setEditing(null)} />
    <PlanApplicationsDrawer plan={viewing} close={() => setViewing(null)} />
  </>;
}
