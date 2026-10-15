import { usePlanCatalog } from '../plans/queries';

export function useOperationsCatalog(enabled = true) {
  return usePlanCatalog('operations', enabled);
}
