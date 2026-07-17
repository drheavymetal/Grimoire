import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { CoverWallItem } from '../domain/types';

// C6 — the wall of covers. `enabled` (default true) lets the Explore hub hold the section folded:
// every cover is an image request behind this one call, so it is the section that most deserves not
// to load until asked.
//
// The default limit is TWELVE, not the forty-eight it opened with. Explore is the one page that
// holds the wall open by default, so its covers are the page's whole load cost: measured 2026-07-17
// on /explore, 48 of the 56 requests at mount were covers, one per release group. Note that
// `Cover.tsx` already renders them `loading="lazy"` and it does not bite — Chrome's prefetch margin
// (~1250px) is taller than the wall, so every image is fetched whether or not it is on screen.
// Twelve is the honest lever; the lazy attribute was never going to be one.
export function useCoverWall(limit = 12, enabled = true) {
  const client = useGrimoireClient();

  return useQuery<CoverWallItem[]>({
    queryKey: ['cover-wall', limit],
    queryFn: ({ signal }) => client.coverWall(limit, signal),
    staleTime: 60_000,
    enabled,
  });
}
