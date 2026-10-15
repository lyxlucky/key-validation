import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Select, Switch, Table, Tag } from 'antd';
import { useAuth } from '../auth/AuthProvider';
import { QueryError, durationText, emptyTable } from '../../components/common';
import type { Application } from '../../lib/types';
import type { ApplicationPlanReference } from './types';
import { useAllGlobalPlans, useApplicationPlanReferences, setPlanReference } from './queries';

export function ApplicationPlanReferences({ application }: { application: Application }) {
  const auth = useAuth();
  const canEdit = auth.isAdmin && auth.access?.allApplications === true && !auth.access.channelId;
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string>();
  const [saving, setSaving] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const cache = useQueryClient();
  const { message } = App.useApp();
  const references = useApplicationPlanReferences(application.id, page);
  const global = useAllGlobalPlans(canEdit);

  async function setState(planId: string, enabled: boolean) {
    if (saving) return;
    setSaving(planId); setError(null);
    try {
      await setPlanReference(cache, application.id, planId, enabled);
      setSelected(undefined);
      message.success(enabled ? '本应用已启用此卡种' : '本应用已暂停此卡种发行');
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : '保存失败，请重试');
    } finally { setSaving(null); }
  }

  return <section>
    <h2 className="panel-title">引用卡种</h2>
    <p className="panel-description">授权规则在卡种管理中统一设置。已有引用再次提交会恢复本应用发行。</p>
    <QueryError error={references.error} retry={references.refetch} />
    {canEdit && <>
      <QueryError error={global.error} retry={global.refetch} />
      <div className="filter-bar reference-filter-bar">
        <Select aria-label="选择全局卡种" showSearch={{ optionFilterProp: 'label' }}
          value={selected} onChange={setSelected} loading={global.isLoading}
          disabled={!!saving || !!global.error}
          placeholder="选择全局卡种" options={global.data?.map(plan => ({
            value: plan.id, label: plan.name + ' · ' + plan.id.slice(0, 8),
          }))} />
        <Button type="primary" disabled={!selected || !!saving || !!global.error}
          loading={saving === selected} onClick={() => { if (selected) void setState(selected, true); }}>
          引用卡种
        </Button>
      </div>
    </>}
    {error && <Alert className="query-error" type="error" showIcon title={error} />}
    <Table<ApplicationPlanReference> rowKey="planId" dataSource={references.data?.items}
      loading={references.isLoading} locale={{ emptyText: emptyTable }} scroll={{ x: 760 }}
      pagination={{ current: page, pageSize: 20, total: references.data?.total,
        showSizeChanger: false, onChange: setPage }} columns={[
        { title: '卡种', dataIndex: 'name', width: 170 },
        { title: '共享规则', render: (_, row) => <>
          <div>{durationText(row.durationSeconds)} · {row.maxDevices} 台设备</div>
          <div>{row.maxConcurrentSessions} 个在线会话 · {row.activationValidityDays == null
            ? '激活期限不限' : '发行后 ' + row.activationValidityDays + ' 天内激活'}</div>
          <div className="muted">{row.features.length ? row.features.join('、') : '无细分功能'}</div>
        </> },
        { title: '全局发行', dataIndex: 'planEnabled', render: value =>
          <Tag color={value ? 'success' : 'default'}>{value ? '可发行' : '已停发'}</Tag> },
        { title: '本应用发行', render: (_, row) => canEdit
          ? <Switch aria-label={'允许本应用发行 ' + row.name} checked={row.isEnabled}
              loading={saving === row.planId} disabled={!!saving || !!references.error}
              onChange={value => { void setState(row.planId, value); }} />
          : row.isEnabled ? '已启用' : '已暂停' },
        { title: '实际状态', dataIndex: 'canIssue', render: value => value ? '可发行' : '不可发行' },
      ]} />
  </section>;
}
