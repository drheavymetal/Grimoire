import { useInfiniteQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { BrowseResult, ThemeKind } from '../domain/types';

// Browse "see all" (2026-07-15): the NAMED door out of a chip. A tag or a theme opens a paged grid
// of the real bands under it. Paging is skip/take over the injected client, accumulated with
// useInfiniteQuery so "load more" appends without refetching earlier pages. Nothing here touches the
// network or the DOM directly (invariant 6): the client is injected, the render lives in ui/.

const PAGE = 48;

// The next skip is the count already loaded, until we have reached the reported total.
function nextSkip(lastPage: BrowseResult, allPages: BrowseResult[]): number | undefined {
  const loaded = allPages.reduce((sum, page) => sum + page.bands.length, 0);
  return loaded < lastPage.total ? loaded : undefined;
}

// B — every band under a raw lowercase tag substring, paged.
export function useBrowseByTag(needle: string) {
  const client = useGrimoireClient();

  return useInfiniteQuery({
    queryKey: ['browse', 'tag', needle],
    queryFn: ({ pageParam, signal }) => client.browseByTag(needle, pageParam, PAGE, signal),
    initialPageParam: 0,
    getNextPageParam: nextSkip,
    enabled: needle.length > 0,
  });
}

// B — every band under a theme (real lyrical, or the C21 mined approximation), paged.
export function useBrowseByTheme(key: string, kind: ThemeKind) {
  const client = useGrimoireClient();

  return useInfiniteQuery({
    queryKey: ['browse', 'theme', kind, key],
    queryFn: ({ pageParam, signal }) => client.browseByTheme(key, kind, pageParam, PAGE, signal),
    initialPageParam: 0,
    getNextPageParam: nextSkip,
    enabled: key.length > 0,
  });
}
