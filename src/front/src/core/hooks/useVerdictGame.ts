import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type {
  AnswerRoundResult,
  VerdictGame,
  VerdictGameAvailability,
  VerdictGameConsent,
  VerdictGameSummary,
  VerdictGuess,
} from '../domain/types';

// The GAMES wave — "did you summon it, or banish it?". Every key starts with ['games'], so one
// invalidation of the prefix refreshes the history and the live game together. Answering also
// touches the OPPONENT's inbox, not the caller's, so nothing here invalidates ['notifications'] —
// the badge that moves is the friend's, and their tab polls for it (D60).

// Whether the caller lets friends play this against their grimoire. `optIn: null` means never asked.
export function useVerdictGameConsent(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<VerdictGameConsent>({
    queryKey: ['games', 'verdict', 'consent'],
    queryFn: ({ signal }) => client.verdictGameConsent(signal),
    enabled,
  });
}

export function useSetVerdictGameConsent() {
  const client = useGrimoireClient();
  const queryClient = useQueryClient();

  return useMutation<void, unknown, boolean>({
    mutationFn: (optIn) => client.setVerdictGameConsent(optIn),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['games'] });
    },
  });
}

// Whether a friend is playable, and the honest reason when not. Asked BEFORE offering to play, so an
// unplayable friend reads as a designed sentence rather than a failed request.
export function useVerdictGameAvailability(friendId: string | null) {
  const client = useGrimoireClient();

  return useQuery<VerdictGameAvailability>({
    queryKey: ['games', 'verdict', 'availability', friendId],
    queryFn: ({ signal }) => client.verdictGameAvailability(friendId!, signal),
    enabled: friendId !== null,
  });
}

// The caller's games, both sides of the turn: played by them, and played against them.
export function useVerdictGames(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<VerdictGameSummary[]>({
    queryKey: ['games', 'verdict', 'list'],
    queryFn: ({ signal }) => client.verdictGames(signal),
    enabled,
    refetchOnWindowFocus: true,
  });
}

// One game — how the console resumes after a reload. Unanswered rounds come back blind.
export function useVerdictGame(gameId: string | null) {
  const client = useGrimoireClient();

  return useQuery<VerdictGame>({
    queryKey: ['games', 'verdict', 'game', gameId],
    queryFn: ({ signal }) => client.verdictGame(gameId!, signal),
    enabled: gameId !== null,
  });
}

export function useStartVerdictGame() {
  const client = useGrimoireClient();
  const queryClient = useQueryClient();

  return useMutation<VerdictGame, unknown, string>({
    mutationFn: (opponentId) => client.startVerdictGame(opponentId),
    onSuccess: (game) => {
      // Seed the cache with the game we just got, so opening the console does not refetch it.
      queryClient.setQueryData(['games', 'verdict', 'game', game.id], game);
      void queryClient.invalidateQueries({ queryKey: ['games', 'verdict', 'list'] });
    },
  });
}

// Answers a round. The result carries the reveal, so the caller renders it directly rather than
// refetching; the game query is invalidated so a resume sees the round as answered.
export function useAnswerVerdictRound(gameId: string | null) {
  const client = useGrimoireClient();
  const queryClient = useQueryClient();

  return useMutation<AnswerRoundResult, unknown, { token: string; verdict: VerdictGuess }>({
    mutationFn: ({ token, verdict }) => client.answerVerdictRound(token, verdict),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['games', 'verdict', 'game', gameId] });
      void queryClient.invalidateQueries({ queryKey: ['games', 'verdict', 'list'] });
    },
  });
}
