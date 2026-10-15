import { Input } from 'antd';

export interface AdvancedFilterDraft {
  from: string; to: string;
  activatedFrom: string; activatedTo: string;
  expiresFrom: string; expiresTo: string;
  deviceId: string;
}

export const emptyAdvancedFilters: AdvancedFilterDraft = {
  from: '', to: '', activatedFrom: '', activatedTo: '', expiresFrom: '', expiresTo: '', deviceId: '',
};

const dates: [keyof AdvancedFilterDraft, string][] = [
  ['from', '发行起始日'], ['to', '发行结束日'],
  ['activatedFrom', '激活起始日'], ['activatedTo', '激活结束日'],
  ['expiresFrom', '到期起始日'], ['expiresTo', '到期结束日'],
];

export function advancedFilterQuery(draft: AdvancedFilterDraft): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [from, to] of [['from', 'to'], ['activatedFrom', 'activatedTo'], ['expiresFrom', 'expiresTo']] as const) {
    if (draft[from] && draft[to] && draft[from] > draft[to]) throw new Error('起始日期不能晚于结束日期');
  }
  for (const [field] of dates) {
    if (!draft[field]) continue;
    const date = new Date(`${draft[field]}T00:00:00`);
    if (!Number.isFinite(date.getTime())) throw new Error('请输入有效日期');
    if (field === 'to' || field.endsWith('To')) date.setDate(date.getDate() + 1);
    result[field] = date.toISOString();
  }
  if (draft.deviceId.trim()) result.deviceId = draft.deviceId.trim();
  return result;
}

export function AdvancedLicenseFilters({ value, onChange }: { value: AdvancedFilterDraft; onChange: (value: AdvancedFilterDraft) => void }) {
  return <section aria-label="高级筛选条件" className="advanced-filter-panel">
    <p className="panel-description">各项条件同时匹配。结束日包含当天，按当前设备时区查询。</p>
    <div className="advanced-filter-grid">
      {dates.map(([field, label]) => <div key={field} className="filter-field">
        <label htmlFor={`license-advanced-${field}`}>{label}</label>
        <Input id={`license-advanced-${field}`} type="date" value={value[field]} onChange={event => onChange({ ...value, [field]: event.target.value })} />
      </div>)}
      <div className="filter-field advanced-device-field">
        <label htmlFor="license-device-filter">设备标识（精确匹配）</label>
        <Input id="license-device-filter" value={value.deviceId} onChange={event => onChange({ ...value, deviceId: event.target.value })} maxLength={256} autoComplete="off" placeholder="先选择应用，再输入完整设备标识" allowClear />
        <span className="muted">同时查找当前及历史绑定。</span>
      </div>
    </div>
  </section>;
}
