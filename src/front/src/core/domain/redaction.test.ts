import { describe, expect, it } from 'vitest';
import { redactionCutForRank, redactionCuts } from './redaction';
import type { Rank } from './types';

const rarestFirst: Rank[] = ['Known', 'Obscure', 'Hidden', 'Forgotten', 'Nameless'];

describe('redactionCutForRank', () => {
  it('corrodes monotonically as bands get rarer', () => {
    const cuts = rarestFirst.map(redactionCutForRank);
    for (let i = 1; i < cuts.length; i += 1) {
      expect(cuts[i]).toBeGreaterThan(cuts[i - 1]);
    }
  });

  it('gives Known the crispest cut and Nameless the most corroded', () => {
    expect(redactionCutForRank('Known')).toBe(Math.min(...redactionCuts));
    expect(redactionCutForRank('Nameless')).toBe(Math.max(...redactionCuts));
  });

  it('returns a real graded cut for every rank', () => {
    for (const rank of rarestFirst) {
      expect(redactionCuts).toContain(redactionCutForRank(rank));
    }
  });
});
