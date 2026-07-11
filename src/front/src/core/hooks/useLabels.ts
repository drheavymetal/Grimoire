import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { LabelDetail, LabelSummary } from '../domain/types';

// B21 — labels as a door. The index, and one label's roster.
export function useLabels() {
  const client = useGrimoireClient();

  return useQuery<LabelSummary[]>({
    queryKey: ['labels'],
    queryFn: ({ signal }) => client.labels(signal),
    staleTime: 60_000,
  });
}

export function useLabel(id: string) {
  const client = useGrimoireClient();

  return useQuery<LabelDetail>({
    queryKey: ['label', id],
    queryFn: ({ signal }) => client.label(id, signal),
    enabled: id.length > 0,
  });
}
