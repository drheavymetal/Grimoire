import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { CoverWallItem } from '../domain/types';

// C6 — the wall of covers.
export function useCoverWall(limit = 48) {
  const client = useGrimoireClient();

  return useQuery<CoverWallItem[]>({
    queryKey: ['cover-wall', limit],
    queryFn: ({ signal }) => client.coverWall(limit, signal),
    staleTime: 60_000,
  });
}
