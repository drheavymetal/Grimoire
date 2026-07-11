import { useMutation } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { DecadeGuess, DecadeScoreResult, DecadeServed } from '../domain/types';

// Serves one scorable band blind for the decade game (feature C27). Returns null when no scorable
// band is in reach (HTTP 204) — a designed empty state, not an error (D25).
export function useServeDecade() {
  const client = useGrimoireClient();

  return useMutation<DecadeServed | null, unknown, number>({
    mutationFn: (comfort) => client.serveDecade(comfort),
  });
}

// Scores a decade-game bet and reveals the band (feature C27). The decade game trains the ear and
// does NOT move the taste vector, so nothing is invalidated — the scoreboard is session-local.
export function useGuessDecade() {
  const client = useGrimoireClient();

  return useMutation<DecadeScoreResult, unknown, { token: string; guess: DecadeGuess }>({
    mutationFn: ({ token, guess }) => client.guessDecade(token, guess),
  });
}
