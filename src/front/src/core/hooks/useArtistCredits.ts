import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { PivotalRelease, ReleaseCredits } from '../domain/types';

// B9 — per-release credits for a band's discography. Portable: the hook only wraps the injected
// client, so it runs unchanged under React Native (D12).
export function useArtistCredits(id: string) {
  const client = useGrimoireClient();

  return useQuery<ReleaseCredits[]>({
    queryKey: ['artists', 'credits', id],
    queryFn: ({ signal }) => client.artistCredits(id, signal),
    enabled: id.length > 0,
    staleTime: 60_000,
  });
}

// B12 — "the disc where everything changed": the release with the most lineup turnover around it.
// Resolves to null when the band's lineup never changed around any dated release.
export function usePivotalRelease(id: string) {
  const client = useGrimoireClient();

  return useQuery<PivotalRelease | null>({
    queryKey: ['artists', 'pivotal-release', id],
    queryFn: ({ signal }) => client.pivotalRelease(id, signal),
    enabled: id.length > 0,
    staleTime: 60_000,
  });
}
