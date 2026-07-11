import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { ComposerDetail } from '../domain/types';

// Loads a composer's works and lineage (movement VII, D11). Fetched only for artists the page has
// already identified as composers (ArtistDetail.hasWorks), so a band never fires this.
export function useComposer(id: string, enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<ComposerDetail>({
    queryKey: ['composers', 'detail', id],
    queryFn: ({ signal }) => client.getComposer(id, signal),
    enabled: enabled && id.length > 0,
  });
}
