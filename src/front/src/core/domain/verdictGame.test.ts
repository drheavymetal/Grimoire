import { describe, expect, it } from 'vitest';
import {
  accuracy,
  currentRound,
  grade,
  isComplete,
  roundNumber,
  stateToVerdict,
  verdictToState,
} from './verdictGame';
import type { GameRound, GameScore, RiteState } from './types';

function round(ordinal: number, answer: RiteState | null): GameRound {
  return {
    token: `token-${ordinal}`,
    ordinal,
    audioUrl: `https://grimoire.test/api/games/rounds/token-${ordinal}/audio`,
    // The blind contract: an unanswered round carries no band and no truth.
    artist: null,
    truth: answer === null ? null : 'Summoned',
    answer,
    correct: answer === null ? null : true,
  };
}

function score(correct: number, answered: number, total: number): GameScore {
  return { correct, answered, total };
}

describe('currentRound', () => {
  it('is the first unanswered round, in deal order', () => {
    const rounds = [round(0, 'Summoned'), round(1, null), round(2, null)];

    expect(currentRound(rounds)?.ordinal).toBe(1);
  });

  it('is null once every round is answered', () => {
    expect(currentRound([round(0, 'Summoned'), round(1, 'Banished')])).toBeNull();
  });

  it('resumes mid-game rather than restarting', () => {
    // What a reload looks like: two played, three to go.
    const rounds = [
      round(0, 'Summoned'),
      round(1, 'Banished'),
      round(2, null),
      round(3, null),
      round(4, null),
    ];

    expect(currentRound(rounds)?.ordinal).toBe(2);
    expect(roundNumber(rounds)).toBe(3);
  });
});

describe('isComplete', () => {
  it('is true only when all rounds are answered', () => {
    expect(isComplete([round(0, 'Summoned'), round(1, 'Banished')])).toBe(true);
    expect(isComplete([round(0, 'Summoned'), round(1, null)])).toBe(false);
  });

  it('is false for a game with no rounds, which is not a finished game', () => {
    expect(isComplete([])).toBe(false);
  });
});

describe('verdict vocabulary', () => {
  it('maps the two buttons to the two rite states', () => {
    expect(verdictToState('summon')).toBe('Summoned');
    expect(verdictToState('banish')).toBe('Banished');
  });

  it('maps the two rite states back to buttons', () => {
    expect(stateToVerdict('Summoned')).toBe('summon');
    expect(stateToVerdict('Banished')).toBe('banish');
  });

  it('has no button for Served or Again — they are not verdicts', () => {
    expect(stateToVerdict('Served')).toBeNull();
    expect(stateToVerdict('Again')).toBeNull();
  });
});

describe('accuracy', () => {
  it('is over what was ANSWERED, not what was dealt', () => {
    // Two of two right, three still unplayed: 100% so far, not 40%.
    expect(accuracy(score(2, 2, 5))).toBe(1);
  });

  it('is null when nothing has been answered', () => {
    // 0% would be a claim about a player who has not played.
    expect(accuracy(score(0, 0, 5))).toBeNull();
  });
});

describe('grade', () => {
  it('calls a full game perfect', () => {
    expect(grade(score(5, 5, 5))).toBe('perfect');
  });

  it('centres "even" on a coin flip, because the question is binary', () => {
    // Half right on a two-way guess means you learned nothing about your friend's ear.
    expect(grade(score(1, 2, 2))).toBe('even');
  });

  it('calls worse than a coin flip poor', () => {
    expect(grade(score(1, 4, 4))).toBe('poor');
  });

  it('calls three of four strong', () => {
    expect(grade(score(3, 4, 4))).toBe('strong');
  });

  it('has no grade before anything is answered', () => {
    expect(grade(score(0, 0, 5))).toBeNull();
  });
});
