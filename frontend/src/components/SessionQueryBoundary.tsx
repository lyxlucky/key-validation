import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// Each authentication surface gets a fresh cache whenever it is mounted.
// The caller changes this component's key when switching admin/portal roots.
export function SessionQueryBoundary({ children }: { children: ReactNode }) {
  const [client] = useState(() => new QueryClient({ defaultOptions: {
    queries: { retry: 1, staleTime: 15000 }, mutations: { retry: false },
  } }));
  useEffect(() => () => client.clear(), [client]);
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
