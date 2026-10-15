import { Alert, Collapse, Descriptions, Drawer, Empty, Table, Tag } from 'antd';
import type { Audit } from '../../lib/types';
import { actions, dateText } from '../../components/common';
import { actorText, auditChanges, auditSummary, methodText, targetText, targetTypes } from './audit-display';

export function AuditResult({ result }: { result: string }) {
  return <Tag color={result === 'failed' ? 'error' : result === 'submitted' ? 'processing' : result === 'succeeded' ? 'success' : 'default'}>{({ succeeded: '成功', failed: '失败', submitted: '已提交' } as Record<string, string>)[result] ?? '未记录'}</Tag>;
}

export function AuditDetail({ log, close }: { log: Audit | null; close: () => void }) {
  const changes = log ? auditChanges(log) : { rows: [], comparison: false };
  let raw = log?.changes ?? '';
  try { raw = JSON.stringify(JSON.parse(raw), null, 2); } catch { /* Historical unstructured data is displayed verbatim. */ }
  return <Drawer title={log?.category === 'login' ? '登录详情' : '操作详情'} open={!!log} onClose={close} size={720} closable={{ 'aria-label': '关闭' }} destroyOnHidden>
    {log && <><div className="audit-detail-heading"><h3>{actions[log.action] ?? '其他操作'}</h3><AuditResult result={log.result} /></div><p className="panel-description">{auditSummary(log)}</p>
      <Descriptions column={1} size="small" items={[
        { key: 'actor', label: '操作人', children: actorText(log) }, { key: 'at', label: '时间', children: dateText(log.createdAt) },
        ...(log.category === 'login' ? [{ key: 'method', label: '登录方式', children: methodText(log) }, { key: 'ip', label: '来源 IP', children: log.sourceIp || '未记录' }] : [
          { key: 'target', label: targetTypes[log.targetType ?? ''] ?? '操作对象', children: targetText(log) },
          { key: 'app', label: '所属应用', children: log.applicationName || (log.applicationId ? '名称未记录' : '平台级操作') },
        ]),
      ]} />
      {log.category !== 'login' && <section className="audit-change-section"><h3>变更内容</h3>
        {!changes.comparison && <Alert type="info" showIcon title="此记录未保存完整的修改前后值，以下仅展示当时记录的内容。" className="query-error" />}
        <Table size="small" rowKey="key" pagination={false} dataSource={changes.rows} locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="此记录没有保存具体字段变化" /> }} columns={[
          { title: '字段', dataIndex: 'label', width: 130 }, ...(changes.comparison ? [{ title: '修改前', dataIndex: 'before', className: 'audit-value' }] : []),
          { title: changes.comparison ? '修改后' : '已记录内容', dataIndex: 'after', className: 'audit-value' },
        ]} />
      </section>}
      <Collapse className="audit-technical-details" items={[{ key: 'technical', label: '追溯信息与原始记录', children: <><Descriptions column={1} size="small" items={[
        { key: 'id', label: '日志 ID', children: <code>{log.id}</code> }, { key: 'actor', label: '操作人 ID', children: <code>{log.actorId}</code> },
        { key: 'target', label: '对象 ID', children: <code>{log.targetId}</code> }, { key: 'trace', label: '请求标识', children: <code>{log.traceId}</code> },
        { key: 'action', label: '事件代码', children: <code>{log.action}</code> },
      ]} /><pre className="audit-raw-data">{raw}</pre></> }]} />
    </>}
  </Drawer>;
}
