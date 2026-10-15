import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Descriptions, Modal, Space, Table, Tag, Tooltip } from 'antd';
import { CloudDownloadOutlined, ReloadOutlined } from '@ant-design/icons';
import { api, queryString } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { BackupJob, BackupStatus, BackupVerification } from '../../lib/reliability-types';
import { bytesText } from '../../lib/reliability-types';
import { dateText, emptyTable, QueryError } from '../../components/common';

const statusNames: Record<string, string> = { running: '执行中', succeeded: '成功', failed: '失败' };
const verificationNames: Record<string, string> = { not_verified: '未验证', running: '验证中', succeeded: '已验证', failed: '验证失败' };

export function BackupsPanel() {
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const [selected, setSelected] = useState<BackupJob | null>(null);
  const [verifying, setVerifying] = useState(false);
  const [result, setResult] = useState<BackupVerification | null>(null);
  const [error, setError] = useState<string | null>(null);
  const { message } = App.useApp();
  const cache = useQueryClient();
  const status = useQuery({ queryKey: ['backup-status'], queryFn: () => api<BackupStatus>('/admin/backups/status'), refetchInterval: 60000 });
  const jobs = useQuery({ queryKey: ['backups', page], queryFn: () => api<Page<BackupJob>>('/admin/backups' + queryString({ page, pageSize: 20 })), refetchInterval: 30000 });
  const refresh = () => cache.invalidateQueries({ predicate: query => ['backups', 'backup-status'].includes(String(query.queryKey[0])) });
  async function create() {
    setCreating(true);
    try {
      const job = await api<BackupJob>('/admin/backups', { method: 'POST' });
      setPage(1); await refresh();
      if (job.status === 'succeeded') message.success('加密备份已创建'); else message.error(`备份未成功：${job.errorCode ?? statusNames[job.status]}`);
    } catch (caught) { message.error((caught as Error).message); }
    finally { setCreating(false); }
  }
  async function verify() {
    if (!selected) return;
    setVerifying(true); setError(null);
    try { setResult(await api<BackupVerification>(`/admin/backups/${selected.id}/verify`, { method: 'POST' })); await refresh(); }
    catch (caught) { setError((caught as Error).message); }
    finally { setVerifying(false); }
  }
  return <><div className="section-toolbar"><p className="muted">创建加密备份，并通过真实恢复验证备份是否可用。</p><Space wrap><Button icon={<ReloadOutlined aria-hidden />} loading={jobs.isFetching} onClick={refresh}>刷新备份</Button><Button type="primary" icon={<CloudDownloadOutlined aria-hidden />} loading={creating} disabled={!status.data?.configured || verifying} onClick={create}>创建备份</Button></Space></div>
    <QueryError error={status.error} retry={status.refetch} /><QueryError error={jobs.error} retry={jobs.refetch} />
    {status.data && <Card className="operations-block"><Descriptions column={{ xs: 1, sm: 2, lg: 3 }} items={[
      { key: 'configured', label: '备份配置', children: <Tag color={status.data.configured ? 'success' : 'warning'}>{status.data.configured ? '已就绪' : '未就绪'}</Tag> },
      { key: 'scheduled', label: '自动备份', children: status.data.scheduleReady ? `每 ${status.data.intervalHours} 小时` : '未启用' }, { key: 'retention', label: '保留成功备份', children: `${status.data.retentionCount} 份` },
      { key: 'last', label: '最近成功', children: dateText(status.data.lastSuccessfulAt) }, { key: 'next', label: '下次自动备份', children: dateText(status.data.nextScheduledAt) }, { key: 'size', label: '单包大小上限', children: bytesText(status.data.maxArchiveBytes) },
    ]} />{!!status.data.configurationErrors.length && <Alert type="warning" showIcon title="请完成备份目录、加密密钥和 PostgreSQL 工具配置" description={status.data.configurationErrors.join('；')} />}</Card>}
    <Alert className="operations-block" type="info" showIcon title="恢复验证会新建独立临时数据库，检查数据与密钥后清理。灾难恢复时，请按运维文档恢复到新库，再人工切换业务连接。" />
    <Card className="data-panel"><Table<BackupJob> rowKey="id" dataSource={jobs.data?.items} loading={jobs.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 1100 }} pagination={{ current: page, pageSize: 20, total: jobs.data?.total, onChange: setPage, showSizeChanger: false }} columns={[
      { title: '创建时间', dataIndex: 'createdAt', render: dateText }, { title: '状态', dataIndex: 'status', render: value => <Tag color={value === 'succeeded' ? 'success' : value === 'failed' ? 'error' : 'processing'}>{statusNames[value] ?? value}</Tag> },
      { title: '大小', dataIndex: 'sizeBytes', render: bytesText }, { title: 'SHA-256', dataIndex: 'sha256', render: value => value ? <Tooltip title={value}><code>{String(value).slice(0, 12)}…</code></Tooltip> : '—' },
      { title: '恢复验证', dataIndex: 'verificationStatus', render: value => verificationNames[value] ?? value }, { title: '验证耗时', dataIndex: 'verificationDurationMilliseconds', render: value => value == null ? '—' : `${(Number(value) / 1000).toFixed(2)} 秒` },
      { title: '错误分类', render: (_, job) => job.errorCode ?? job.verificationErrorCode ?? '—' }, { title: '操作', width: 115, fixed: 'right', render: (_, job) => <Button type="link" disabled={job.status !== 'succeeded' || creating || verifying} onClick={() => { setSelected(job); setResult(null); setError(null); }}>验证恢复</Button> },
    ]} /></Card>
    <Modal title={result ? '恢复验证结果' : '验证恢复'} open={!!selected} onCancel={() => { if (!verifying) setSelected(null); }} closable={{ 'aria-label': '关闭', disabled: verifying }} maskClosable={!verifying} footer={result ? <Button type="primary" onClick={() => setSelected(null)}>完成</Button> : <Space><Button disabled={verifying} onClick={() => setSelected(null)}>取消</Button><Button type="primary" loading={verifying} onClick={verify}>开始验证</Button></Space>}>
      {error && <Alert type="error" showIcon title={error} className="query-error" />}
      {result ? <><Alert className="operations-block" type={result.status === 'succeeded' ? 'success' : 'error'} showIcon title={result.status === 'succeeded' ? '备份已通过真实恢复验证' : '恢复验证失败'} description={result.errorCode} /><Descriptions column={1} items={[
        { key: 'time', label: '验证耗时', children: `${(Number(result.durationMilliseconds) / 1000).toFixed(2)} 秒` }, { key: 'migrations', label: '恢复的迁移记录', children: result.migrationCount }, { key: 'keys', label: '密钥材料', children: result.keysReadable ? '可读取' : '未验证' },
      ]} /></> : <p>将恢复 {dateText(selected?.createdAt)} 创建的备份，验证迁移、关键数据表和密钥材料。整个过程使用新建的临时数据库。</p>}
    </Modal>
  </>;
}
