import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { SeedCandidate } from '../domain/types';

// The bands to pick from on the cold-start "choose five" screen (D15). Not blind: these
// are bands the user already knows, whose embeddings seed the taste vector.
export function useSeedCandidates(enabled: boolean, limit = 60) {
  const client = useGrimoireClient();

  return useQuery<SeedCandidate[]>({
    queryKey: ['rite', 'seed-candidates', limit],
    queryFn: ({ signal }) => client.seedCandidates(limit, signal),
    enabled,
    staleTime: 5 * 60_000,
  });
}
