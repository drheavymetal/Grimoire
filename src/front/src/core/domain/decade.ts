// Pure, portable scoreboard maths for "guess the decade" (feature C27). No DOM, no platform
// coupling — a plain reducer the UI and its tests both call. The backend scores each round; the
// front only accumulates the running tally across the session (no persistence, no migration).

import type { DecadeScoreResult } from './types';

export interface Scoreboard {
  // How many rounds have been played this session.
  rounds: number;
  // Points earned across all rounds.
  points: number;
  // The most points those rounds could have earned (rounds * per-round max).
  maxPoints: number;
}

export const EMPTY_SCOREBOARD: Scoreboard = { rounds: 0, points: 0, maxPoints: 0 };

// Folds one scored round into the running scoreboard. Pure: it returns a new object and never
// mutates its input, so React state updates stay honest and the reducer is trivially testable.
export function addRound(board: Scoreboard, result: DecadeScoreResult): Scoreboard {
  return {
    rounds: board.rounds + 1,
    points: board.points + result.totalPoints,
    maxPoints: board.maxPoints + result.maxPoints,
  };
}

// The list of decades a player can bet, newest first. Derived from a "now" year so it needs no
// external data; each option is the decade's start year, matching what the backend scores against
// (it normalises any year in a decade to the decade's start).
export function decadeOptions(nowYear: number, earliest = 1960): number[] {
  const latest = Math.floor(nowYear / 10) * 10;
  const decades: number[] = [];

  for (let d = latest; d >= earliest; d -= 10) {
    decades.push(d);
  }

  return decades;
}
