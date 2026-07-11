import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { DuelResult, DuelServed, ServeFilters } from '../domain/types';

// Serves two bands blind for a duel (feature C2). Returns null when the ring cannot supply two
// distinct bands (HTTP 204) — a designed empty state, not an error (D25).
export function useDuel() {
  const client = useGrimoireClient();

  return useMutation<DuelServed | null, unknown, ServeFilters>({
    mutationFn: (filters) => client.duel(filters),
  });
}

// Resolves a duel with the winner the user preferred (feature C2). On success the taste and
// grimoire queries are invalidated: the winner enters the grimoire and the taste vector moves
// toward it and away from the loser server-side (Bradley-Terry).
export function useResolveDuel() {
  const client = useGrimoireClient();
  const queryClient = useQueryClient();

  return useMutation<DuelResult, unknown, { winnerToken: string; loserToken: string }>({
    mutationFn: ({ winnerToken, loserToken }) => client.resolveDuel(winnerToken, loserToken),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['rite', 'taste'] });
      void queryClient.invalidateQueries({ queryKey: ['rite', 'grimoire'] });
    },
  });
}
