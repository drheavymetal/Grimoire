import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { ArtistSummary } from '../domain/types';

// Search hook. Only queries once the (debounced) term has at least two characters,
// so a single keystroke does not hammer the trigram index.
export function useArtistSearch(term: string, limit = 20) {
  const client = useGrimoireClient();
  const trimmed = term.trim();
  const enabled = trimmed.length >= 2;

  return useQuery<ArtistSummary[]>({
    queryKey: ['artists', 'search', trimmed, limit],
    queryFn: ({ signal }) => client.searchArtists(trimmed, limit, signal),
    enabled,
    staleTime: 30_000,
  });
}
