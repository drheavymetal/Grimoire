import { useMutation, useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { SeedCandidate } from '../domain/types';

// The bands to pick from on the cold-start "choose five" screen (D15). Not blind: these
// are bands the user already knows, whose embeddings seed the taste vector.
//
// Fetched once and never refetched on a pick: the grid is stable by design, and grows through
// useRelatedSeeds instead (see core/domain/seedGrid.ts for why).
export function useSeedCandidates(enabled: boolean, limit = 60) {
  const client = useGrimoireClient();

  return useQuery<SeedCandidate[]>({
    queryKey: ['rite', 'seed-candidates', limit],
    queryFn: ({ signal }) => client.seedCandidates(limit, signal),
    enabled,
    staleTime: 5 * 60_000,
  });
}

// The neighbours of one picked band, to unfold underneath it. Asks for more than the grid will show,
// because the caller drops the ones it already has on screen.
export function useRelatedSeeds(limit = 24) {
  const client = useGrimoireClient();

  return useMutation<SeedCandidate[], Error, string>({
    mutationFn: (artistId) => client.relatedSeeds(artistId, limit),
  });
}
