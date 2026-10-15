import { Button, Dropdown, Grid, Space, Tag } from 'antd';
import { api } from '../../lib/api';
import type { Page } from '../../lib/types';
import type { OrderDelivery } from '../../lib/commerce-types';
import type { DeliveryView } from './DeliveryDialog';

export function money(cents: number | string) {
  const integer = BigInt(cents), absolute = integer < 0n ? -integer : integer;
  return `${integer < 0n ? '-' : ''}¥${new Intl.NumberFormat('zh-CN').format(absolute / 100n)}.${String(absolute % 100n).padStart(2, '0')}`;
}
export const maximumUnitPriceCents = Math.floor(Number.MAX_SAFE_INTEGER / 100);
export const orderTypeNames: Record<string, string> = { issue: '发卡', renewal: '续期' };
export const requestTypeNames: Record<string, string> = { renewal: '续期申请', rebinding: '换绑申请' };
export const orderStatusNames: Record<string, string> = { pending: '待履约', fulfilled: '已履约', cancelled: '已取消' };
export const requestStatusNames: Record<string, string> = { pending: '待商家处理', approved: '已批准', rejected: '已拒绝' };
export function OrderStatus({ status }: { status: string }) { return <Tag color={{ pending: 'processing', fulfilled: 'success', cancelled: 'default' }[status]}>{orderStatusNames[status] ?? status}</Tag>; }
export function RequestStatus({ status }: { status: string }) { return <Tag color={{ pending: 'processing', approved: 'success', rejected: 'error' }[status]}>{requestStatusNames[status] ?? status}</Tag>; }
export function deliveryView(value: OrderDelivery): DeliveryView { return { ...value, licenses: value.licenses.map(item => ({ licenseId: item.id, keyHint: item.keyHint, key: item.key })) }; }

export async function allPages<T>(path: string): Promise<T[]> {
  const items: T[] = [];
  for (let page = 1; ; page++) {
    const result = await api<Page<T>>(`${path}${path.includes('?') ? '&' : '?'}page=${page}&pageSize=100`);
    items.push(...result.items);
    if (!result.items.length || items.length >= result.total) return items;
  }
}

export interface RowAction { key: string; label: string; onClick: () => void; danger?: boolean; disabled?: boolean }
export function RowActions({ items }: { items: RowAction[] }) {
  const screens = Grid.useBreakpoint();
  return screens.md ? <Space size={0} wrap>{items.map(item => <Button key={item.key} type="link" danger={item.danger} disabled={item.disabled} onClick={item.onClick}>{item.label}</Button>)}</Space>
    : <Dropdown trigger={['click']} menu={{ items: items.map(item => ({ ...item })) }}><Button type="link">操作</Button></Dropdown>;
}
