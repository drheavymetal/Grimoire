import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { RareInstrument } from '../domain/types';

// C15 — rare instruments: the folk/orchestral colour outside the standard rock kit, and who plays
// each. Portable hook: only wraps the injected client (D12). `enabled` (default true) lets a folded
// section skip the call.
export function useRareInstruments(enabled = true) {
  const client = useGrimoireClient();

  return useQuery<RareInstrument[]>({
    queryKey: ['instruments', 'rare'],
    queryFn: ({ signal }) => client.rareInstruments(signal),
    staleTime: 60_000,
    enabled,
  });
}
