import { describe, expect, it } from 'vitest';
import { hasCredits, splitPerformers } from './credits';
import type { PerformerCredit } from './types';

function performer(name: string, isGuest: boolean): PerformerCredit {
  return { artistId: name, name, rank: null, instruments: ['guitar'], isGuest };
}

describe('splitPerformers', () => {
  it('sends members and guests to their own groups (the D9 distinction)', () => {
    const { members, guests } = splitPerformers([
      performer('Member A', false),
      performer('Guest B', true),
      performer('Member C', false),
    ]);

    // Inverting the isGuest check would swap these two — that is the bite.
    expect(members.map((m) => m.name)).toEqual(['Member A', 'Member C']);
    expect(guests.map((g) => g.name)).toEqual(['Guest B']);
  });

  it('handles a release with no performers', () => {
    const { members, guests } = splitPerformers([]);
    expect(members).toEqual([]);
    expect(guests).toEqual([]);
  });
});

describe('hasCredits', () => {
  it('is false only when there is nothing to show', () => {
    expect(hasCredits({ performers: [], production: [] })).toBe(false);
    expect(hasCredits({ performers: [performer('X', false)], production: [] })).toBe(true);
    expect(hasCredits({ performers: [], production: [{}] })).toBe(true);
  });
});
