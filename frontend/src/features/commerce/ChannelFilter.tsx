import { useQuery } from '@tanstack/react-query';
import { Select } from 'antd';
import type { Channel } from '../../lib/commerce-types';
import { QueryError } from '../../components/common';
import { useAuth } from '../auth/AuthProvider';
import { allPages } from './commerce-shared';

export function ChannelFilter({ id, value, onChange }: { id: string; value?: string; onChange: (value: string | undefined) => void }) {
  const { isAdmin } = useAuth();
  const channels = useQuery({ queryKey: ['channels', 'all'], queryFn: () => allPages<Channel>('/admin/channels'), enabled: isAdmin });
  if (!isAdmin) return null;
  return <div className="filter-field"><label htmlFor={id}>所属渠道</label>
    <Select id={id} allowClear showSearch={{ optionFilterProp: 'label' }} value={value} placeholder="全部渠道" loading={channels.isLoading} options={channels.data?.map(channel => ({ value: channel.id, label: channel.name }))} onChange={onChange} />
    <QueryError error={channels.error} retry={channels.refetch} />
  </div>;
}
