import { useEffect, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Input, Select, Space, Table, Tag } from 'antd';
import { DownloadOutlined, PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { Link } from 'react-router-dom';
import { api, errorCodeText, queryString } from '../../lib/api';
import { downloadText } from '../../lib/download';
import type { Page } from '../../lib/types';
import type { OfflineConfiguration, OfflineLicense, SignedOfflineLicense } from '../../lib/client-extension-types';
import { offlineStatusNames } from '../../lib/client-extension-types';
import { dateText, emptyTable, PageTitle, QueryError } from '../../components/common';
import { SecretDialog } from '../../components/SecretDialog';
import { useAuth } from '../auth/AuthProvider';
import { useOperationsCatalog } from '../operations/useOperationsCatalog';
import { OfflineSignDialog } from './OfflineSignDialog';
import type { OfflineSignValues } from './OfflineSignDialog';

export function OfflineLicensesPage() {
  const [page, setPage] = useState(1);
  const [applicationId, setApplicationId] = useState<string>();
  const [licenseDraft, setLicenseDraft] = useState('');
  const [licenseId, setLicenseId] = useState('');
  const [signOpen, setSignOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [issued, setIssued] = useState<SignedOfflineLicense | null>(null);
  const { hasPermission } = useAuth();
  const canManage = hasPermission('licenses.manage');
  const { message, modal } = App.useApp();
  const cache = useQueryClient();
  const catalog = useOperationsCatalog();
  const configuration = useQuery({ queryKey: ['offline-configuration'], queryFn: () => api<OfflineConfiguration>('/admin/offline-licenses/configuration') });
  const records = useQuery({
    queryKey: ['offline-licenses', applicationId, licenseId, page],
    queryFn: () => api<Page<OfflineLicense>>('/admin/offline-licenses' + queryString({ applicationId, licenseId, page, pageSize: 20 })),
  });
  useEffect(() => { if (records.data) setPage(current => Math.min(current, Math.max(1, Math.ceil(records.data.total / 20)))); }, [records.data]);
  const config = configuration.data;
  const ready = !!config?.enabled && config.configured;
  const appName = (id: string) => catalog.data?.applications.find(app => app.id === id)?.name ?? id;
  function filterLicense() {
    if (licenseDraft.trim() && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(licenseDraft.trim())) {
      message.error('请输入完整的授权 ID'); return;
    }
    setLicenseId(licenseDraft.trim()); setPage(1);
  }
  const refresh = () => cache.invalidateQueries({ predicate: query => ['offline-licenses', 'audit'].includes(String(query.queryKey[0])) });
  async function sign(values: OfflineSignValues) {
    if (saving) return;
    setSaving(true); setError(null);
    try {
      // The signed content stays in this component's memory until explicitly cleared.
      const result = await api<SignedOfflineLicense>('/admin/offline-licenses', { method: 'POST', body: {
        licenseId: values.licenseId.trim(), bindingId: values.bindingId.trim(), deviceId: values.deviceId, hours: values.hours,
      } });
      setSignOpen(false); setIssued(result); setPage(1);
      await refresh();
    } catch (caught) { setError((caught as Error).message); }
    finally { setSaving(false); }
  }
  function revoke(record: OfflineLicense) {
    modal.confirm({
      title: '撤销这份离线授权？',
      content: '撤销后，在线核验会立即拒绝此记录。已经离线的设备仍可能使用至许可证到期。',
      okText: '确认撤销', cancelText: '取消', okButtonProps: { danger: true },
      onOk: async () => {
        try { await api('/admin/offline-licenses/' + record.id + '/revoke', { method: 'POST' }); await refresh(); message.success('离线授权已撤销'); }
        catch (caught) { message.error((caught as Error).message); throw caught; }
      },
    });
  }
  return <><PageTitle title="离线授权" subtitle="为已激活的正式授权签发限时离线许可证，查看签发记录和撤销状态。" extra={<Space wrap>
    <Button icon={<ReloadOutlined aria-hidden />} loading={records.isFetching || configuration.isFetching} onClick={() => Promise.all([records.refetch(), configuration.refetch()])}>刷新记录</Button>
    {canManage && <Button type="primary" icon={<PlusOutlined aria-hidden />} disabled={!ready} onClick={() => { setError(null); setSignOpen(true); }}>签发离线授权</Button>}
  </Space>} />
    <Alert className="operations-block" type="warning" showIcon title="离线撤销存在延迟" description={config?.limitations ?? '离线设备无法即时获知撤销、冻结、禁用或解绑；最长延迟为许可证剩余有效期。'} />
    <QueryError error={configuration.error} retry={configuration.refetch} />
    {config && <Card className="operations-block" size="small"><div className="panel-heading"><div>
      <h2 className="panel-title">签名配置 <Tag color={ready ? 'success' : 'warning'}>{ready ? '可签发' : config.enabled ? '未配置完成' : '已关闭'}</Tag></h2>
      <p className="panel-description">{ready ? '当前密钥：' + config.currentKeyId + ' · 默认 ' + config.defaultHours + ' 小时，最长 ' + config.maxHours + ' 小时' : config.errorCode ? errorCodeText(config.errorCode) : '请由管理员启用离线授权并配置签名密钥。'}</p>
    </div><Button icon={<DownloadOutlined aria-hidden />} disabled={!Object.keys(config.publicKeys ?? {}).length} onClick={() => downloadText(JSON.stringify(config.publicKeys, null, 2), 'keygate-trusted-offline-keys.json', 'application/json;charset=utf-8')}>导出验签公钥</Button></div>
      <p className="panel-description">将公钥通过可信的软件发布流程部署到客户端。许可证文件不提供可信公钥；私钥只保存在服务端。</p>
    </Card>}
    <QueryError error={catalog.error} retry={catalog.refetch} /><QueryError error={records.error} retry={records.refetch} />
    <Card className="data-panel"><div className="operations-content operations-filter">
      <div className="filter-field"><label htmlFor="offline-application">离线授权应用</label><Select id="offline-application" allowClear value={applicationId} showSearch={{ optionFilterProp: 'label' }} placeholder="全部可见应用"
        options={catalog.data?.applications.map(app => ({ value: app.id, label: app.name }))} onChange={value => { setApplicationId(value); setPage(1); }} /></div>
      <div className="filter-field"><label htmlFor="offline-license">授权 ID</label><Input id="offline-license" value={licenseDraft} onChange={event => setLicenseDraft(event.target.value)} allowClear maxLength={36} placeholder="按完整授权 ID 查找" onPressEnter={filterLicense} /></div>
      <Button onClick={filterLicense}>筛选记录</Button>
    </div><Table<OfflineLicense> rowKey="id" dataSource={records.data?.items} loading={records.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 1540 }}
      pagination={{ current: page, pageSize: 20, total: records.data?.total, onChange: setPage, showSizeChanger: false, showTotal: total => '共 ' + total + ' 条签发记录' }} columns={[
        { title: '授权', width: 240, render: (_, record) => <div><Link to={'/licenses?licenseId=' + record.licenseId}>{record.licenseHint}</Link><div><code className="break-word">{record.licenseId}</code></div></div> },
        { title: '应用', width: 170, render: (_, record) => <div>{appName(record.applicationId)}<div className="muted">{record.applicationCode}</div></div> },
        { title: '绑定设备', width: 230, render: (_, record) => <div>{record.deviceLabel || '未命名设备'}<div><code className="break-word">{record.bindingId}</code></div></div> },
        { title: '状态', width: 110, dataIndex: 'status', render: value => <Tag color={value === 'active' ? 'success' : value === 'revoked' || value === 'superseded' ? 'default' : 'warning'}>{offlineStatusNames[value] ?? value}</Tag> },
        { title: '授权功能', width: 160, dataIndex: 'features', render: features => <div className="license-tags">{(features as string[] | null)?.length ? (features as string[]).map(feature => <Tag key={feature}>{feature}</Tag>) : '未配置细分功能'}</div> },
        { title: '签发时间', width: 180, dataIndex: 'issuedAt', render: dateText },
        { title: '到期时间', width: 180, dataIndex: 'expiresAt', render: dateText },
        { title: '签名密钥', width: 150, dataIndex: 'kid', render: value => <code className="break-word">{value}</code> },
        { title: '操作', width: 90, fixed: 'right', render: (_, record) => canManage && !record.revokedAt && new Date(record.expiresAt).getTime() > Date.now() ? <Button type="link" danger onClick={() => revoke(record)}>撤销</Button> : '—' },
      ]} /></Card>
    {canManage && <OfflineSignDialog open={signOpen} busy={saving} error={error} save={sign} close={() => { setSignOpen(false); setError(null); }} />}
    <SecretDialog title="保存离线许可证" value={issued?.licenseContent ?? null} fileName={'keygate-' + (issued?.record?.serial ?? 'offline') + '.kglicense'}
      description="请下载并交付给对应设备。关闭后无法重新取回该文件；再次签发会替代旧记录。许可证仅在签名、应用、设备、时间和所需功能均符合时授予离线权限。" close={() => setIssued(null)} />
  </>;
}
