import { useQuery } from '@tanstack/react-query';
import { ArrowRightOutlined, AppstoreOutlined, CheckCircleOutlined, DesktopOutlined, KeyOutlined, PlusOutlined, SafetyCertificateOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { Button, Card, Table } from 'antd';
import { useNavigate } from 'react-router-dom';
import { api } from '../../lib/api';
import type { Dashboard, License, Page } from '../../lib/types';
import { dateText, emptyTable, LicenseStatus, PageTitle, QueryError } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';

export function OverviewPage() {
  const navigate = useNavigate();
  const { canIssue, hasPermission } = useAuth();
  const canReadLicenses = hasPermission('licenses.read');
  const stats = useQuery({ queryKey: ['dashboard'], queryFn: () => api<Dashboard>('/admin/dashboard'), refetchInterval: 30000 });
  const recent = useQuery({ queryKey: ['licenses', 'recent'], queryFn: () => api<Page<License>>('/admin/licenses?page=1&pageSize=5'), enabled: canReadLicenses });
  const metrics = [
    { title: '卡密总量', value: stats.data?.totalLicenses, icon: <KeyOutlined />, text: '所有应用累计发行', label: 'TOTAL LICENSES' },
    { title: '已激活卡密', value: stats.data?.activatedLicenses, icon: <ThunderboltOutlined />, text: '已完成首次设备激活', label: 'ACTIVATED' },
    { title: '有效授权', value: stats.data?.validLicenses, icon: <CheckCircleOutlined />, text: '当前可用的卡密授权', label: 'VALID LICENSES' },
    { title: '在线会话', value: stats.data?.onlineSessions, icon: <DesktopOutlined />, text: '租约有效的客户端会话', label: 'ONLINE SESSIONS' },
  ];
  return <><PageTitle title="概览" subtitle="授权运行情况一目了然，开始今天的管理工作。" extra={canIssue && <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/licenses?generate=1')}>生成卡密</Button>} />
    <QueryError error={stats.error} retry={stats.refetch} />
    <div className="metrics-grid">{metrics.map((metric, i) => <Card key={metric.title} className={'metric-card metric-card-' + i} loading={stats.isLoading}><div className="metric-top"><span>{metric.title}</span><span className={'metric-icon metric-' + i}>{metric.icon}</span></div><strong>{metric.value?.toLocaleString() ?? '—'}</strong><div className="metric-bottom"><p>{metric.text}</p><span>{metric.label}</span></div></Card>)}</div>
    {canReadLicenses && <div className="overview-grid">
      <Card className="data-panel recent-panel" title={<div className="panel-title"><KeyOutlined /><span>最近生成的卡密</span></div>} extra={<Button type="link" onClick={() => navigate('/licenses')}>查看全部 <ArrowRightOutlined /></Button>}>
        <div className="panel-description">最近 5 张卡密的发行与激活情况</div><QueryError error={recent.error} retry={recent.refetch} />
        <Table<License> rowKey="id" loading={recent.isLoading} dataSource={recent.data?.items} pagination={false} scroll={{ x: 500 }} locale={{ emptyText: emptyTable }} columns={[
          { title: '卡密标识', dataIndex: 'keyHint', render: value => <code className="code-chip">{value}</code> },
          { title: '状态', dataIndex: 'status', render: value => <LicenseStatus status={value} /> },
          { title: '创建时间', dataIndex: 'createdAt', render: dateText },
        ]} />
      </Card>
      <Card className="workflow-card"><span className="workflow-badge"><SafetyCertificateOutlined /> 授权工作流</span><h3>从发行到验证，<br />每一步都有迹可循。</h3><p className="workflow-description">在一个工作空间里，管理完整授权周期。</p><ol>
        <li><span className="step-number">01</span><div><strong>配置应用与卡种</strong><span>定义授权时长与设备数量</span></div></li>
        <li><span className="step-number">02</span><div><strong>生成并分发卡密</strong><span>保存本次发行的卡密明文</span></div></li>
        <li><span className="step-number">03</span><div><strong>管理设备与会话</strong><span>查看绑定、心跳和授权状态</span></div></li>
      </ol><Button block icon={<AppstoreOutlined />} onClick={() => navigate('/applications')}>前往应用管理 <ArrowRightOutlined /></Button></Card>
    </div>}
    {hasPermission('audit.read') && <div className="overview-note"><span className="note-icon"><SafetyCertificateOutlined /></span><div><strong>清晰的记录，可靠的管理</strong><p>应用配置、卡密签发与授权变更，均可在操作日志中追踪。</p></div><Button type="link" onClick={() => navigate('/audit')}>查看操作日志 <ArrowRightOutlined aria-hidden /></Button></div>}
  </>;
}

