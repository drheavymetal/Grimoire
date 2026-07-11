import { describe, expect, it } from 'vitest';
import { addRound, decadeOptions, EMPTY_SCOREBOARD } from './decade';
import type { DecadeScoreResult } from './types';

// The session scoreboard reducer (feature C27). These bite: they assert the running tally is the
// SUM of the rounds and that the input is never mutated. Break the accumulation and they fail.

function round(points: number, maxPoints: number): DecadeScoreResult {
  return {
    // Only the score fields matter to the reducer; the rest is filler for the type.
    artist: {} as DecadeScoreResult['artist'],
    decade: { guess: '', actual: '', outcome: 'miss', points: 0 },
    country: { guess: '', actual: '', outcome: 'miss', points: 0 },
    subgenre: { guess: '', actual: '', outcome: 'miss', points: 0 },
    totalPoints: points,
    maxPoints,
  };
}

describe('addRound', () => {
  it('accumulates points, rounds and the max across rounds', () => {
    let board = EMPTY_SCOREBOARD;
    board = addRound(board, round(4, 4));
    board = addRound(board, round(1, 4));
    board = addRound(board, round(2, 4));

    expect(board.rounds).toBe(3);
    expect(board.points).toBe(7); // 4 + 1 + 2 — break the sum and this fails
    expect(board.maxPoints).toBe(12); // 3 rounds * 4
  });

  it('does not mutate the input board', () => {
    const before = { rounds: 2, points: 5, maxPoints: 8 };
    const after = addRound(before, round(3, 4));

    expect(before).toEqual({ rounds: 2, points: 5, maxPoints: 8 });
    expect(after).toEqual({ rounds: 3, points: 8, maxPoints: 12 });
    expect(after).not.toBe(before);
  });
});

describe('decadeOptions', () => {
  it('lists decades newest first, from the current decade back to the earliest', () => {
    const options = decadeOptions(2026, 1990);
    expect(options).toEqual([2020, 2010, 2000, 1990]);
  });

  it('starts at the current decade, not the exact year', () => {
    expect(decadeOptions(2009, 2000)).toEqual([2000]);
  });
});
