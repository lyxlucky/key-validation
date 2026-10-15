import { Drawer, Table, Tag } from 'antd';
import { QueryError, dateText, emptyTable } from '../../components/common';
import { usePlanApplications } from './queries';
import type { GlobalPlan, PlanReference } from './types';

export function PlanApplicationsDrawer({ plan, close }: {
  plan: GlobalPlan | null; close: () => void;
}) {
  const references = usePlanApplications(plan?.id);
  return <Drawer open={!!plan} title={(plan?.name ?? '') + ' · 关联应用'}
    size={640} onClose={close} closable={{ 'aria-label': '关闭' }}>
    <QueryError error={references.error} retry={references.refetch} />
    <Table<PlanReference> rowKey="applicationId" dataSource={references.data}
      loading={references.isLoading} locale={{ emptyText: emptyTable }}
      pagination={{ pageSize: 20 }} columns={[
        { title: '应用', dataIndex: 'applicationName' },
        { title: '本应用发行', dataIndex: 'isEnabled', render: value =>
          <Tag color={value ? 'success' : 'default'}>{value ? '已启用' : '已暂停'}</Tag> },
        { title: '引用时间', dataIndex: 'createdAt', render: dateText },
      ]} />
  </Drawer>;
}
