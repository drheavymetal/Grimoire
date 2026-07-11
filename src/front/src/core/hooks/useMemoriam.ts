import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { MemoriamEntry } from '../domain/types';

// C12 — In Memoriam: the musicians in the grimoire who have died, chronological. Portable hook.
export function useMemoriam() {
  const client = useGrimoireClient();

  return useQuery<MemoriamEntry[]>({
    queryKey: ['memoriam'],
    queryFn: ({ signal }) => client.memoriam(signal),
    staleTime: 60_000,
  });
}
