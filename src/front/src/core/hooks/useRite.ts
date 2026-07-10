import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { ResolveResult, RiteAction, ServedRite, ServeFilters } from '../domain/types';

// Serves one band blind (feature B13/B14). Returns the served rite, or null when the ring
// is empty (HTTP 204) — a designed empty state, not an error (D25).
export function useServe() {
  const client = useGrimoireClient();

  return useMutation<ServedRite | null, unknown, ServeFilters>({
    mutationFn: (filters) => client.serve(filters),
  });
}

// Resolves a served rite with summon / banish / again (features B13, C3, C4). On success
// the taste and grimoire queries are invalidated: a summon grows the grimoire and both a
// summon and a banish move the taste/repulsion vectors server-side.
export function useResolve() {
  const client = useGrimoireClient();
  const queryClient = useQueryClient();

  return useMutation<ResolveResult, unknown, { token: string; action: RiteAction }>({
    mutationFn: ({ token, action }) => client.resolve(token, action),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['rite', 'taste'] });
      void queryClient.invalidateQueries({ queryKey: ['rite', 'grimoire'] });
    },
  });
}
