import { describe, expect, it } from 'vitest';
import {
  currentGuessRound,
  guessAccuracy,
  guessGrade,
  guessRoundNumber,
  isChallenge,
  isGuessComplete,
  isTypedRound,
} from './guessGame';
import type { GuessGame, GuessRound, GuessScore } from './types';

function round(ordinal: number, correct: boolean | null, typed = false): GuessRound {
  return {
    token: `token-${ordinal}`,
    ordinal,
    audioUrl: `https://grimoire.test/api/games/guess/rounds/token-${ordinal}/audio`,
    // The blind contract: an unanswered round carries no band. In `hard` it carries no names either.
    choices: typed
      ? null
      : [
          { artistId: 'a', name: 'Darkthrone' },
          { artistId: 'b', name: 'Burzum' },
          { artistId: 'c', name: 'Mayhem' },
          { artistId: 'd', name: 'Emperor' },
        ],
    artist: correct === null ? null : { id: 'a', name: 'Darkthrone', country: 'NO', formedYear: 1986, rank: 'Known' },
    correct,
  };
}

function score(correct: number, answered: number, total: number, pointsPerRound = 1): GuessScore {
  return { correct, answered, total, points: correct * pointsPerRound, pointsPerRound };
}

function game(opponentId: string | null): GuessGame {
  return {
    id: 'game',
    difficulty: 'Normal',
    opponentId,
    opponentHandle: opponentId === null ? null : 'pedro',
    status: 'InProgress',
    createdAt: '2026-07-17T00:00:00Z',
    finishedAt: null,
    rounds: [],
    score: score(0, 0, 5),
  };
}

describe('currentGuessRound', () => {
  it('is the first unanswered round, in deal order', () => {
    expect(currentGuessRound([round(0, true), round(1, null), round(2, null)])?.ordinal).toBe(1);
  });

  it('is null once every round is answered', () => {
    expect(currentGuessRound([round(0, true), round(1, false)])).toBeNull();
  });

  // A WRONG answer still closes its round. `correct: false` is an answer, not an absence — reading it
  // as "unanswered" would loop the player back onto a round they have already lost.
  it('treats a wrong answer as answered, not as unplayed', () => {
    expect(currentGuessRound([round(0, false), round(1, null)])?.ordinal).toBe(1);
  });

  it('is null for a game with no rounds', () => {
    expect(currentGuessRound([])).toBeNull();
  });
});

describe('isGuessComplete', () => {
  it('is true only once every round is answered', () => {
    expect(isGuessComplete([round(0, true), round(1, false)])).toBe(true);
    expect(isGuessComplete([round(0, true), round(1, null)])).toBe(false);
  });

  // An empty game is not a finished one: it would show the player a scoreboard for a game nobody played.
  it('is false for a game with no rounds', () => {
    expect(isGuessComplete([])).toBe(false);
  });
});

describe('guessRoundNumber', () => {
  it('is 1-based, for display', () => {
    expect(guessRoundNumber([round(0, true), round(1, null)])).toBe(2);
  });

  it('is the round count once the game is done', () => {
    expect(guessRoundNumber([round(0, true), round(1, true)])).toBe(2);
  });
});

describe('isTypedRound', () => {
  // Null choices and an EMPTY list are different claims: "nothing is offered here, type it" versus
  // "four names exist and none arrived", which would be a bug. A falsy check would hide the second.
  it('is a typed round only when no choices were offered at all', () => {
    expect(isTypedRound(round(0, null, true))).toBe(true);
    expect(isTypedRound(round(0, null))).toBe(false);
    expect(isTypedRound({ ...round(0, null), choices: [] })).toBe(false);
  });
});

describe('guessAccuracy', () => {
  it('is right over answered', () => {
    expect(guessAccuracy(score(3, 4, 5))).toBe(0.75);
  });

  // A player who has answered nothing has no accuracy. 0% would be a claim about them.
  it('is null when nothing was answered', () => {
    expect(guessAccuracy(score(0, 0, 5))).toBeNull();
  });
});

describe('guessGrade', () => {
  it('grades on what was answered, not on what was dealt', () => {
    // Two of two right mid-game is a perfect run so far, even with three rounds still to play.
    expect(guessGrade(score(2, 2, 5))).toBe('perfect');
  });

  it('has a band for every outcome', () => {
    expect(guessGrade(score(5, 5, 5))).toBe('perfect');
    expect(guessGrade(score(3, 5, 5))).toBe('strong');
    expect(guessGrade(score(2, 5, 5))).toBe('half');
    expect(guessGrade(score(0, 5, 5))).toBe('poor');
  });

  it('is null when nothing was answered', () => {
    expect(guessGrade(score(0, 0, 5))).toBeNull();
  });
});

describe('isChallenge', () => {
  // Solo is the ordinary case, not a degraded one — and nobody is told about it.
  it('separates a solo game from one sent to a friend', () => {
    expect(isChallenge(game(null))).toBe(false);
    expect(isChallenge(game('friend-id'))).toBe(true);
  });
});
