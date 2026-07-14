import { describe, expect, it } from 'vitest';
import {
  BASE_REDACTION_CUT,
  redactionCutForRank,
  redactionCuts,
  redactionFontFamily,
  revealCutSequence,
} from './redaction';
import type { Rank } from './types';

const knownToNameless: Rank[] = ['Known', 'Obscure', 'Hidden', 'Forgotten', 'Nameless'];

describe('redactionCutForRank', () => {
  it('corrodes monotonically as bands get rarer (the cut number rises)', () => {
    const cuts = knownToNameless.map(redactionCutForRank);
    for (let i = 1; i < cuts.length; i += 1) {
      expect(cuts[i]).toBeGreaterThan(cuts[i - 1]);
    }
  });

  it('gives Known the cleanest cut (10) and Nameless the most corroded static cut', () => {
    expect(redactionCutForRank('Known')).toBe(Math.min(...redactionCuts));
    // Nameless is the most eroded a static name gets, but capped below the illegible cut 100.
    expect(redactionCutForRank('Nameless')).toBe(70);
    expect(redactionCutForRank('Nameless')).toBeLessThan(Math.max(...redactionCuts));
  });

  it('returns a real graded cut for every rank', () => {
    for (const rank of knownToNameless) {
      expect(redactionCuts).toContain(redactionCutForRank(rank));
    }
  });

  it('renders an unknown rank at the clean base, never corroded (unknown is not rare)', () => {
    expect(redactionCutForRank(null)).toBe(BASE_REDACTION_CUT);
    expect(redactionCutForRank(null)).toBe(Math.min(...redactionCuts));
  });
});

describe('revealCutSequence', () => {
  it('emerges most-corroded and resolves down to a clean Known (10)', () => {
    expect(revealCutSequence(redactionCutForRank('Known'))).toEqual([100, 70, 50, 35, 20, 10]);
  });

  it('a Nameless resolves only to its capped eroded cut, never clearing', () => {
    expect(revealCutSequence(redactionCutForRank('Nameless'))).toEqual([100, 70]);
  });

  it('starts most-corroded and ends on the target for every rank', () => {
    for (const rank of knownToNameless) {
      const target = redactionCutForRank(rank);
      const seq = revealCutSequence(target);
      expect(seq[0]).toBe(Math.max(...redactionCuts));
      expect(seq[seq.length - 1]).toBe(target);
      for (let i = 1; i < seq.length; i += 1) {
        expect(seq[i]).toBeLessThan(seq[i - 1]);
      }
    }
  });
});

describe('redactionFontFamily', () => {
  it('names the graded face for the cut and keeps legible fallbacks', () => {
    expect(redactionFontFamily(100)).toBe("'Redaction 100', 'Redaction', Georgia, serif");
    expect(redactionFontFamily(10)).toContain("'Redaction 10'");
    expect(redactionFontFamily(10)).toContain('serif');
  });
});
