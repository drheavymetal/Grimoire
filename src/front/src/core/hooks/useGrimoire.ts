import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { GrimoireEntry } from '../domain/types';

// The bands the user has summoned, newest first — their grimoire (feature C17 data).
export function useGrimoire(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<GrimoireEntry[]>({
    queryKey: ['rite', 'grimoire'],
    queryFn: ({ signal }) => client.grimoire(signal),
    enabled,
  });
}
