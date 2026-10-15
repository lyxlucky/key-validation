import { Tabs } from 'antd';
import { PageTitle } from '../../components/common';
import { MonitoringPanel } from './MonitoringPanel';
import { BackupsPanel } from './BackupsPanel';

export function OperationsPage() {
  return <><PageTitle title="运维中心" subtitle="查看验证服务的实际运行情况，管理备份与恢复验证。" />
    <Tabs items={[{ key: 'monitoring', label: '服务监控', children: <MonitoringPanel /> }, { key: 'backups', label: '备份与恢复', children: <BackupsPanel /> }]} />
  </>;
}
