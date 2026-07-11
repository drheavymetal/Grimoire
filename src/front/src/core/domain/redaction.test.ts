import { describe, expect, it } from 'vitest';
import { redactionCutForRank, redactionCuts } from './redaction';
import type { Rank } from './types';

const knownToNameless: Rank[] = ['Known', 'Obscure', 'Hidden', 'Forgotten', 'Nameless'];

describe('redactionCutForRank', () => {
  it('corrodes monotonically as bands get rarer (the cut number drops)', () => {
    const cuts = knownToNameless.map(redactionCutForRank);
    for (let i = 1; i < cuts.length; i += 1) {
      expect(cuts[i]).toBeLessThan(cuts[i - 1]);
    }
  });

  it('gives Known the crispest cut (100) and Nameless the most corroded (10)', () => {
    expect(redactionCutForRank('Known')).toBe(Math.max(...redactionCuts));
    expect(redactionCutForRank('Nameless')).toBe(Math.min(...redactionCuts));
  });

  it('returns a real graded cut for every rank', () => {
    for (const rank of knownToNameless) {
      expect(redactionCuts).toContain(redactionCutForRank(rank));
    }
  });
});
