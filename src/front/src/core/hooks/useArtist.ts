import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { ArtistDetail } from '../domain/types';

// Loads a single artist with its releases and edges.
export function useArtist(id: string) {
  const client = useGrimoireClient();

  return useQuery<ArtistDetail>({
    queryKey: ['artists', 'detail', id],
    queryFn: ({ signal }) => client.getArtist(id, signal),
    enabled: id.length > 0,
  });
}
