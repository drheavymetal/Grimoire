import type { GameRound, GameScore, RiteState, VerdictGuess } from './types';

// The GAMES wave — pure logic of "did you summon it, or banish it?". No DOM, no adapters (invariant
// 6): the console only paints what these return, and a test exercises the rules without a browser.

// Which round the player is on: the first one not yet answered, in deal order. Null when the game is
// done. Resuming after a reload is the same question as starting, which is why this is one function.
export function currentRound(rounds: readonly GameRound[]): GameRound | null {
  return rounds.find((round) => round.answer === null) ?? null;
}

// Whether every round has been answered. Derived from the rounds rather than trusted from the
// server's status, so a stale status can never strand the player on a finished game.
export function isComplete(rounds: readonly GameRound[]): boolean {
  return rounds.length > 0 && rounds.every((round) => round.answer !== null);
}

// How far in, for the "round 2 of 5" counter. 1-based for display; 0 when there is nothing to play.
export function roundNumber(rounds: readonly GameRound[]): number {
  const current = currentRound(rounds);

  return current === null ? rounds.length : current.ordinal + 1;
}

// The verdict a player's button press means, in the server's vocabulary.
export function verdictToState(guess: VerdictGuess): RiteState {
  return guess === 'summon' ? 'Summoned' : 'Banished';
}

// The button that WOULD have been right, for reading back a finished round.
export function stateToVerdict(state: RiteState): VerdictGuess | null {
  if (state === 'Summoned') {
    return 'summon';
  }

  if (state === 'Banished') {
    return 'banish';
  }

  // Served and Again are not verdicts: they are never a round's truth, so they have no button.
  return null;
}

// The score as a percentage, for the closing verdict. Null when nothing was answered — a game with
// no answers has no accuracy, and 0% would be a claim about a player who never played.
export function accuracy(score: GameScore): number | null {
  if (score.answered === 0) {
    return null;
  }

  return score.correct / score.answered;
}

// How the final score reads back, as a key the UI translates. The bands are about how well you know
// one person's ear — not about music trivia, which is the whole distinction the game exists for.
export type VerdictGameGradeKey = 'perfect' | 'strong' | 'even' | 'poor';

// A coin flip scores ~50% on a binary question, so "even" is centred there rather than at zero:
// guessing half right means you learned nothing about your friend, not that you did half well.
export function grade(score: GameScore): VerdictGameGradeKey | null {
  const rate = accuracy(score);

  if (rate === null) {
    return null;
  }

  if (rate === 1) {
    return 'perfect';
  }

  if (rate >= 0.75) {
    return 'strong';
  }

  if (rate >= 0.5) {
    return 'even';
  }

  return 'poor';
}
