import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { CompareResult, Graph, OneAlbumBand, ProlificBand } from '../domain/types';

// Each hook takes `enabled` (default true) so a caller that keeps the component mounted but folded
// away — the Explore hub's collapsible sections — pays nothing until the reader asks. Same shape as
// useReleaseTracks: disabled queries fire nothing until they are wanted.

// C24 — bands with exactly one album and nothing else.
export function useOneAlbumBands(enabled = true) {
  const client = useGrimoireClient();

  return useQuery<OneAlbumBand[]>({
    queryKey: ['catalogue', 'one-album'],
    queryFn: ({ signal }) => client.oneAlbumBands(signal),
    staleTime: 60_000,
    enabled,
  });
}

// C25 — bands that released more than they have lived.
export function useHyperprolific(enabled = true) {
  const client = useGrimoireClient();

  return useQuery<ProlificBand[]>({
    queryKey: ['catalogue', 'hyperprolific'],
    queryFn: ({ signal }) => client.hyperprolific(signal),
    staleTime: 60_000,
    enabled,
  });
}

// C9 — the split graph (bands joined by a shared split release).
export function useSplits(enabled = true) {
  const client = useGrimoireClient();

  return useQuery<Graph>({
    queryKey: ['splits'],
    queryFn: ({ signal }) => client.splits(signal),
    staleTime: 60_000,
    enabled,
  });
}

// B24 — compare two bands. Disabled until both ids are present and distinct.
export function useCompare(a: string, b: string) {
  const client = useGrimoireClient();

  return useQuery<CompareResult>({
    queryKey: ['compare', a, b],
    queryFn: ({ signal }) => client.compare(a, b, signal),
    enabled: a.length > 0 && b.length > 0 && a !== b,
  });
}
