import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Descriptions, Drawer, Empty, Space, Spin, Table, Tag, Typography } from 'antd';
import { Link } from 'react-router-dom';
import { CheckCircleOutlined, ClockCircleOutlined, DesktopOutlined, DisconnectOutlined, KeyOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import { api } from '../../lib/api';
import type { LicenseDetail } from '../../lib/types';
import { dateText, durationText, LicenseStatus, QueryError } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';
import { useLicenseOperations } from '../operations/LicenseOperations';
import { DevicePolicyPanel } from './DevicePolicyPanel';
import { FreezePolicyPanel } from './FreezePolicyPanel';
import { CopyValue } from '../../components/CopyValue';

export function LicenseDrawer({ id, close }: { id: string | null; close: () => void }) {
  const { canManage, access, hasPermission } = useAuth();
  const { openOperation } = useLicenseOperations();
  const { message, modal } = App.useApp();
  const cache = useQueryClient();
  const detail = useQuery({ queryKey: ['license', id], queryFn: () => api<LicenseDetail>(`/admin/licenses/${id}`), enabled: !!id, refetchInterval: id ? 30000 : false });
  const license = detail.data?.license;
  function confirm(title: string, content: string, path: string, okText: string, danger = true) {
    modal.confirm({ title, content, okText, cancelText: '取消', okButtonProps: { danger }, onOk: async () => {
      try { await api(path, { method: 'POST' }); await cache.invalidateQueries(); message.success('操作已完成'); } catch (e) { message.error((e as Error).message); throw e; }
    } });
  }
  return <Drawer title="卡密详情" open={!!id} closable={{ 'aria-label': '关闭' }} onClose={close} size={850}>
    <QueryError error={detail.error} retry={detail.refetch} />{detail.isLoading ? <div className="drawer-loading"><Spin /></div> : license && <>
      <div className="detail-summary license-summary"><div className="license-summary-main"><span className="overlay-icon"><KeyOutlined /></span><div><span className="summary-label">卡密标识</span><code className="license-summary-key">{license.keyHint}</code><LicenseStatus status={license.status!} /></div></div>{canManage && <Button danger={!license.disabledAt} onClick={() => confirm(license.disabledAt ? '恢复这张卡密？' : '禁用这张卡密？', license.disabledAt ? '有效期不会延长，客户端需要重新激活。' : '所有现有会话将被撤销，客户端不能继续激活或续租。', `/admin/licenses/${id}/${license.disabledAt ? 'enable' : 'disable'}`, license.disabledAt ? '确认恢复' : '确认禁用', !license.disabledAt)}>{license.disabledAt ? '恢复卡密' : '禁用卡密'}</Button>}</div>
      <h3 className="section-heading license-section-heading"><SafetyCertificateOutlined /> 授权信息</h3>
      {access?.channelId && <Alert className="query-error" type="info" showIcon title="渠道卡密需通过续期订单处理" action={hasPermission('orders.manage') ? <Link to="/orders">前往订单管理</Link> : undefined} />}
      {canManage && <Space className="license-detail-actions" wrap>{!access?.channelId && <Button type="primary" disabled={!!license.frozenAt} onClick={() => openOperation('extend', [license])}>续期卡密</Button>}<Button onClick={() => openOperation('metadata', [license])}>编辑备注标签</Button></Space>}
      <Descriptions className="license-descriptions" bordered size="small" column={{ xs: 1, sm: 2, md: 2, lg: 2, xl: 2, xxl: 2 }} items={[
        { key: 'id', label: '卡密 ID', children: <CopyValue value={license.id} label="卡密 ID" />, span: 'filled' },
        { key: 'duration', label: '授权时长', children: durationText(license.durationSeconds!) }, { key: 'devices', label: '设备上限', children: `${license.maxDevices} 台` },
        { key: 'kind', label: '授权类型', children: license.isTrial ? '试用授权' : '正式授权' },
        { key: 'concurrent', label: '在线会话上限', children: `${license.maxConcurrentSessions} 个` },
        { key: 'activateBefore', label: '最晚激活时间', children: license.activateBefore ? dateText(license.activateBefore) : '不限', span: 'filled' },
        { key: 'features', label: '授权功能', children: <div className="license-tags">{license.features?.length ? license.features.map(feature => <Tag key={feature}>{feature}</Tag>) : '未配置细分功能'}</div>, span: 'filled' },
        { key: 'created', label: '创建时间', children: dateText(license.createdAt) }, { key: 'activated', label: '激活时间', children: dateText(license.activatedAt) },
        { key: 'expires', label: license.frozenAt ? '冻结前到期时间' : '到期时间', children: dateText(license.expiresAt), span: 'filled' },
        { key: 'note', label: '卡密备注', children: <span className="break-word">{license.note || '—'}</span>, span: 'filled' },
        { key: 'tags', label: '标签', children: <div className="license-tags">{license.tags?.length ? license.tags.map(tag => <Tag key={tag}>{tag}</Tag>) : '—'}</div>, span: 'filled' },
      ]} />
      <FreezePolicyPanel key={`freeze:${license.id}`} license={license} />
      <h3 className="section-heading license-section-heading"><DesktopOutlined /> 设备绑定 <span className="count-badge">{detail.data?.bindings?.filter(x => !x.releasedAt).length ?? 0} / {license.maxDevices}</span></h3>
      <p className="panel-description binding-description">查看绑定设备及其会话，解绑后将释放设备名额。</p>
      {!detail.data?.bindings?.length && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="此卡密尚未绑定设备" />}
      {detail.data?.bindings?.map(binding => <Card key={binding.id} size="small" className="binding-card" title={<div className="binding-title"><DesktopOutlined /><span>{binding.deviceLabel || '未命名设备'}</span><Tag className="status-tag" color={binding.releasedAt ? 'default' : 'blue'}>{binding.releasedAt ? '已解绑' : '已绑定'}</Tag></div>} extra={canManage && !binding.releasedAt && <Button size="small" danger onClick={() => confirm('解绑此设备？', '此设备所有会话将被撤销，并释放一个设备名额。卡密有效期不变。', `/admin/device-bindings/${binding.id}/release`, '确认解绑')}>解绑设备</Button>}>
        <p className="muted binding-timestamp">绑定于 {dateText(binding.boundAt)}</p><p className="break-word">绑定 ID：<Typography.Text code copyable>{binding.id}</Typography.Text></p><h4 className="sessions-heading">设备会话 <span>{binding.sessions?.length ?? 0}</span></h4><Table rowKey="id" size="small" dataSource={binding.sessions ?? []} pagination={false} locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="此设备暂无会话" /> }} scroll={{ x: 560 }} columns={[
          { title: '会话状态', render: (_, session) => <Tag className="status-tag" icon={session.revokedAt ? <DisconnectOutlined /> : new Date(session.leaseExpiresAt!).getTime() > Date.now() ? <CheckCircleOutlined /> : <ClockCircleOutlined />} color={session.revokedAt ? 'default' : new Date(session.leaseExpiresAt!).getTime() > Date.now() ? 'success' : 'warning'}>{session.revokedAt ? '已撤销' : new Date(session.leaseExpiresAt!).getTime() > Date.now() ? '在线' : '已失效'}</Tag> },
          { title: '最近心跳', dataIndex: 'lastSeenAt', render: dateText }, { title: '租约到期', dataIndex: 'leaseExpiresAt', render: dateText },
          { title: '操作', render: (_, session) => canManage && !session.revokedAt ? <Button type="link" danger onClick={() => confirm('撤销此会话？', '客户端需要重新激活才能继续使用。设备绑定将保留。', `/admin/sessions/${session.id}/revoke`, '确认撤销')}>撤销</Button> : '—' },
        ]} />
      </Card>)}
      <DevicePolicyPanel key={`device:${license.id}`} licenseId={license.id} />
    </>}
  </Drawer>;
}
