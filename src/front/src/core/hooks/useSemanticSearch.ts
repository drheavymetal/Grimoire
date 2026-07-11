import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { SemanticHit } from '../domain/types';

// B2 — free-text semantic search. Only fires once the (debounced) query has some substance, so a
// single keystroke does not hit the embedding service. A 503 (engine unavailable) surfaces as an
// error the page reports honestly, never a faked ranking.
export function useSemanticSearch(query: string, enabled: boolean, limit = 20) {
  const client = useGrimoireClient();
  const trimmed = query.trim();

  return useQuery<SemanticHit[]>({
    queryKey: ['semantic', trimmed, limit],
    queryFn: ({ signal }) => client.semanticSearch(trimmed, limit, signal),
    enabled: enabled && trimmed.length >= 3,
    staleTime: 30_000,
    retry: false,
  });
}
