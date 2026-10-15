import { useQuery } from '@tanstack/react-query';
import { api } from '../../lib/api';

export interface DeveloperDocumentSummary { id: string; title: string; topic: string; version: string }
export interface DeveloperDocument extends DeveloperDocumentSummary { markdown: string }
export function useDeveloperDocuments() {
  return useQuery({ queryKey: ['developer-docs'], queryFn: () => api<DeveloperDocumentSummary[]>('/integrations/developer-docs') });
}
export function useDeveloperDocument(id: string, version: string) {
  return useQuery({ queryKey: ['developer-docs', id, version], queryFn: () => api<DeveloperDocument>(`/integrations/developer-docs/${encodeURIComponent(id)}`) });
}
