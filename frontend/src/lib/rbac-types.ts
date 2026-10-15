import type { Dto, Page } from './types';
import { api, queryString } from './api';

export type Role = Dto<'RoleDto'>;
export type Permission = Dto<'PermissionDto'>;

export async function allRoles(): Promise<Role[]> {
  const roles: Role[] = [];
  for (let page = 1; ; page++) {
    const result = await api<Page<Role>>('/admin/roles' + queryString({ page, pageSize: 100 }));
    roles.push(...result.items);
    if (roles.length >= result.total || !result.items.length) return roles;
  }
}
