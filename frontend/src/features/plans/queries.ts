import { useQuery } from '@tanstack/react-query';
import type { QueryClient } from '@tanstack/react-query';
import { api, queryString } from '../../lib/api';
import type { Page, Plan } from '../../lib/types';
import type { OperationsCatalog } from '../../lib/operations-types';
import type { ApplicationPlanReference, GlobalPlan, PlanReference, PlanUpdate } from './types';

async function allPlanPages<T>(path: string): Promise<T[]> {
  const items: T[] = [];
  for (let page = 1; ; page++) {
    const result = await api<Page<T>>(path + queryString({ page, pageSize: 100 }));
    items.push(...result.items);
    if (!result.items.length || items.length >= Number(result.total)) return items;
  }
}

export function useGlobalPlans(page: number, search = '', isEnabled?: boolean) {
  return useQuery({
    queryKey: ['plans', 'global', page, search, isEnabled],
    queryFn: () => api<Page<GlobalPlan>>('/admin/plans' +
      queryString({ page, pageSize: 20, search: search || undefined,
        isEnabled: isEnabled === undefined ? undefined : String(isEnabled) })),
  });
}

export function useAllGlobalPlans(enabled: boolean) {
  return useQuery({
    queryKey: ['plans', 'global-options'],
    queryFn: () => allPlanPages<GlobalPlan>('/admin/plans'), enabled,
  });
}

export function useApplicationPlans(applicationId?: string, enabled = true) {
  return useQuery({
    queryKey: ['plans', 'application-options', applicationId],
    queryFn: () => allPlanPages<Plan>('/admin/applications/' + applicationId + '/plans'),
    enabled: enabled && !!applicationId,
  });
}

export function useApplicationPlanReferences(applicationId: string, page: number) {
  return useQuery({
    queryKey: ['plans', 'references', applicationId, page],
    queryFn: () => api<Page<ApplicationPlanReference>>(
      '/admin/applications/' + applicationId + '/plan-references' +
      queryString({ page, pageSize: 20 })),
  });
}

export function usePlanApplications(planId?: string) {
  return useQuery({
    queryKey: ['plans', 'applications', planId],
    queryFn: () => api<PlanReference[]>('/admin/plans/' + planId + '/applications'),
    enabled: !!planId,
  });
}

export function usePlanCatalog(kind: 'operations' | 'reports' = 'operations', enabled = true) {
  return useQuery({
    queryKey: [kind === 'reports' ? 'operations-catalog' : 'operations-access-catalog'],
    queryFn: () => api<OperationsCatalog>(
      kind === 'reports' ? '/admin/reports/catalog' : '/admin/catalog'),
    enabled,
  });
}

export async function invalidatePlanQueries(cache: QueryClient) {
  const roots = ['plans', 'operations-access-catalog', 'operations-catalog',
    'batches', 'batch', 'operations-report', 'channel', 'channels', 'audit'];
  await Promise.all(roots.map(root => cache.invalidateQueries({ queryKey: [root] })));
}

export async function savePlan(cache: QueryClient, id: string | undefined, body: PlanUpdate) {
  const result = await api<GlobalPlan>(id ? '/admin/plans/' + id : '/admin/plans', {
    method: id ? 'PATCH' : 'POST', body,
  });
  await invalidatePlanQueries(cache);
  return result;
}

export async function setPlanReference(cache: QueryClient, applicationId: string,
  planId: string, isEnabled: boolean) {
  const result = await api<ApplicationPlanReference>(
    '/admin/applications/' + applicationId + '/plan-references/' + planId,
    { method: 'PUT', body: { isEnabled } });
  await invalidatePlanQueries(cache);
  return result;
}
