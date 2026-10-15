import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Card, Empty, Select, Spin, Tag } from 'antd';
import { api } from '../../lib/api';
import type { UpdateVersionPolicy, VersionPolicy } from '../../lib/client-extension-types';
import { PageTitle, QueryError } from '../../components/common';
import { useOperationsCatalog } from '../operations/useOperationsCatalog';
import { VersionPolicyForm } from './VersionPolicyForm';
import type { VersionFormValues } from './VersionPolicyForm';

export function ClientPoliciesPage() {
  const [applicationId, setApplicationId] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { message } = App.useApp();
  const cache = useQueryClient();
  const catalog = useOperationsCatalog();
  const policy = useQuery({
    queryKey: ['version-policy', applicationId], enabled: !!applicationId,
    queryFn: () => api<VersionPolicy>('/admin/applications/' + applicationId + '/version-policy'),
  });
  async function save(values: VersionFormValues) {
    if (!applicationId || saving) return;
    setSaving(true); setError(null);
    const body: UpdateVersionPolicy = {
      minimumVersion: values.minimumVersion?.trim() || null,
      latestVersion: values.latestVersion?.trim() || null,
      enforceMinimum: values.enforceMinimum,
      updateUrl: values.updateUrl?.trim() || null,
      releaseNotes: values.releaseNotes?.trim() || null,
      sha256: values.sha256?.trim() || null,
    };
    try {
      const saved = await api<VersionPolicy>('/admin/applications/' + applicationId + '/version-policy', { method: 'PUT', body });
      cache.setQueryData(['version-policy', applicationId], saved);
      await cache.invalidateQueries({ queryKey: ['audit'] });
      message.success('版本策略已保存');
    } catch (caught) { setError((caught as Error).message); }
    finally { setSaving(false); }
  }
  return <><PageTitle title="客户端版本" subtitle="发布更新信息，按应用设置最低支持版本与升级提示。" />
    <QueryError error={catalog.error} retry={catalog.refetch} />
    <Card className="operations-block"><div className="operations-filter">
      <div className="filter-field"><label htmlFor="version-application">版本策略应用</label><Select id="version-application" value={applicationId} disabled={saving} loading={catalog.isLoading}
        showSearch={{ optionFilterProp: 'label' }} placeholder="选择要配置的应用" options={catalog.data?.applications.map(app => ({ value: app.id, label: app.name }))}
        onChange={value => { setApplicationId(value); setError(null); }} /></div>
    </div></Card>
    <QueryError error={policy.error} retry={policy.refetch} />
    {!applicationId ? <Card><Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="选择应用后配置客户端版本策略" /></Card>
      : policy.isLoading ? <Card><Spin description="正在读取版本策略…" /></Card>
      : policy.data && <Card title="版本与更新策略" extra={<Tag color={policy.data.enforceMinimum ? 'orange' : 'blue'}>{policy.data.enforceMinimum ? '已启用最低版本限制' : '兼容旧客户端'}</Tag>}>
        <VersionPolicyForm key={applicationId} initial={{
          minimumVersion: policy.data.minimumVersion ?? undefined, latestVersion: policy.data.latestVersion ?? undefined,
          enforceMinimum: policy.data.enforceMinimum, updateUrl: policy.data.updateUrl ?? undefined,
          releaseNotes: policy.data.releaseNotes ?? undefined, sha256: policy.data.sha256 ?? undefined,
        }} busy={saving} error={error} save={save} />
      </Card>}
  </>;
}
