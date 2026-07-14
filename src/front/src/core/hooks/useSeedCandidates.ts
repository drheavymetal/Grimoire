import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { SeedCandidate } from '../domain/types';

// The bands to pick from on the cold-start "choose five" screen (D15). Not blind: these
// are bands the user already knows, whose embeddings seed the taste vector.
//
// The grid answers the picks — pass them and it refills with their neighbours (pick Judas Priest,
// Iron Maiden and Venom arrive). Previous data is kept while the next grid loads, so the chips do
// not blink out from under a finger mid-pick.
export function useSeedCandidates(enabled: boolean, picked: string[] = [], limit = 60) {
  const client = useGrimoireClient();

  // Sorted so the same set of picks is the same cache key regardless of the order they were clicked.
  const key = [...picked].sort();

  return useQuery<SeedCandidate[]>({
    queryKey: ['rite', 'seed-candidates', limit, key],
    queryFn: ({ signal }) => client.seedCandidates(limit, key, signal),
    enabled,
    staleTime: 5 * 60_000,
    placeholderData: keepPreviousData,
  });
}
