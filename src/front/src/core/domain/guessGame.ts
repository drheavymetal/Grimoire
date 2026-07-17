import type { GuessGame, GuessRound, GuessScore } from './types';

// Guess the band (D67) — the pure logic of "you loved it blind; do you even know who it is?". No DOM,
// no adapters (invariant 6): the console only paints what these return, and a test exercises the
// rules without a browser.

// Which round the player is on: the first not yet answered, in deal order. Null when the game is
// done. Resuming after a reload is the same question as starting, which is why this is one function.
//
// `correct` is the marker rather than an answer field, and that is not an accident of naming: this
// game has no stored answer to look at. A band does not fit in the column the verdict game answers
// into, and truncating one to fit would have filed a different fact under its name. `correct` is
// decided server-side against the real band, and its presence is exactly "this round is done".
export function currentGuessRound(rounds: readonly GuessRound[]): GuessRound | null {
  return rounds.find((round) => round.correct === null) ?? null;
}

// Whether every round has been answered. Derived from the rounds rather than trusted from the
// server's status, so a stale status can never strand the player on a finished game.
export function isGuessComplete(rounds: readonly GuessRound[]): boolean {
  return rounds.length > 0 && rounds.every((round) => round.correct !== null);
}

// How far in, for the "round 2 of 5" counter. 1-based for display; 0 when there is nothing to play.
export function guessRoundNumber(rounds: readonly GuessRound[]): number {
  const current = currentGuessRound(rounds);

  return current === null ? rounds.length : current.ordinal + 1;
}

// Whether a round is the typed one. `choices` null means the server offered nothing to pick from —
// which is what `hard` IS. An empty list would be a different claim (four names, none of them sent),
// so the two are kept apart rather than collapsed into a falsy check.
export function isTypedRound(round: GuessRound): boolean {
  return round.choices === null;
}

// The score as a percentage, for the closing verdict. Null when nothing was answered — a game with no
// answers has no accuracy, and 0% would be a claim about a player who never played.
export function guessAccuracy(score: GuessScore): number | null {
  if (score.answered === 0) {
    return null;
  }

  return score.correct / score.answered;
}

// How the final score reads back, as a key the UI translates. The bands are about the joke the game
// is built on: you already chose these bands with your ears, blind. Knowing their names is a
// different skill, and the app's whole argument is that it is the lesser one — so even "poor" is not
// an insult here, and the copy must not read like one.
export type GuessGradeKey = 'perfect' | 'strong' | 'half' | 'poor';

// Centred on what each mode actually costs. It is deliberately NOT re-centred for `normal`'s one-in-
// four freebie: the grade describes what you knew, and the points are where the two modes are priced
// apart (`pointsPerRound`). Two knobs for one fact would drift.
export function guessGrade(score: GuessScore): GuessGradeKey | null {
  const rate = guessAccuracy(score);

  if (rate === null) {
    return null;
  }

  if (rate === 1) {
    return 'perfect';
  }

  if (rate >= 0.6) {
    return 'strong';
  }

  if (rate >= 0.3) {
    return 'half';
  }

  return 'poor';
}

// Whether this game was sent to somebody. Solo is the ordinary case and it must not read as a
// degraded one: it is a game against your own record, and nobody is told.
export function isChallenge(game: GuessGame): boolean {
  return game.opponentId !== null;
}
