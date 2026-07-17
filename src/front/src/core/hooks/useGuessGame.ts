import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type {
  AnswerGuessRoundResult,
  GuessDifficulty,
  GuessGame,
  GuessGameAvailability,
  GuessGameSummary,
} from '../domain/types';

// Guess the band (D67). Every key starts with ['games', 'guess'], under the same ['games'] prefix the
// verdict game uses, so one invalidation of the prefix refreshes both games' lists together.
//
// Answering only ever touches the OPPONENT's inbox, never the caller's, so nothing here invalidates
// ['notifications'] — the badge that moves is the friend's, and their tab polls for it (D60).

// Whether the caller's OWN grimoire can make a game at this difficulty. Asked before offering to
// play, so an unplayable grimoire renders an honest sentence rather than a failed request. It is not
// per-friend, and that absence is the design: a challenge never reads the friend's grimoire.
export function useGuessGameAvailability(difficulty: GuessDifficulty, enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<GuessGameAvailability>({
    queryKey: ['games', 'guess', 'availability', difficulty],
    queryFn: ({ signal }) => client.guessGameAvailability(difficulty, signal),
    enabled,
  });
}

// The caller's guess games, both sides of the turn: played by them, and challenges sent to them.
export function useGuessGames(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<GuessGameSummary[]>({
    queryKey: ['games', 'guess', 'list'],
    queryFn: ({ signal }) => client.guessGames(signal),
    enabled,
    refetchOnWindowFocus: true,
  });
}

// One game — how the console resumes after a reload. Unanswered rounds come back blind, and their
// four names come back in the same order they were first served.
export function useGuessGame(gameId: string | null) {
  const client = useGrimoireClient();

  return useQuery<GuessGame>({
    queryKey: ['games', 'guess', 'game', gameId],
    queryFn: ({ signal }) => client.guessGame(gameId!, signal),
    enabled: gameId !== null,
  });
}

export function useStartGuessGame() {
  const client = useGrimoireClient();
  const queryClient = useQueryClient();

  return useMutation<GuessGame, unknown, { difficulty: GuessDifficulty; opponentId: string | null }>({
    mutationFn: ({ difficulty, opponentId }) => client.startGuessGame(difficulty, opponentId),
    onSuccess: (game) => {
      // Seed the cache with the game we just got, so opening the console does not refetch it.
      queryClient.setQueryData(['games', 'guess', 'game', game.id], game);
      void queryClient.invalidateQueries({ queryKey: ['games', 'guess', 'list'] });
    },
  });
}

// Answers a round, by picked name or by typed name. One mutation for both because which one is legal
// is the GAME's fact, not the caller's: the server decides from the difficulty it dealt, and a
// wrongly-shaped body is a 400 rather than a free round.
export function useAnswerGuessRound(gameId: string | null) {
  const client = useGrimoireClient();
  const queryClient = useQueryClient();

  return useMutation<
    AnswerGuessRoundResult,
    unknown,
    { token: string; artistId?: string; name?: string }
  >({
    mutationFn: ({ token, artistId, name }) =>
      artistId !== undefined
        ? client.answerGuessRoundByChoice(token, artistId)
        : client.answerGuessRoundByName(token, name ?? ''),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['games', 'guess', 'game', gameId] });
      void queryClient.invalidateQueries({ queryKey: ['games', 'guess', 'list'] });
    },
  });
}
