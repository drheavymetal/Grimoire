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

  it('renders an unknown rank at the crisp base, never corroded (unknown is not rare)', () => {
    expect(redactionCutForRank(null)).toBe(BASE_REDACTION_CUT);
    expect(redactionCutForRank(null)).toBe(Math.max(...redactionCuts));
  });
});

describe('revealCutSequence', () => {
  it('walks the whole ladder up to a crisp Known (100)', () => {
    expect(revealCutSequence(redactionCutForRank('Known'))).toEqual([10, 20, 35, 50, 70, 100]);
  });

  it('never resolves a Nameless: a single most-corroded frame', () => {
    expect(revealCutSequence(redactionCutForRank('Nameless'))).toEqual([10]);
  });

  it('starts corroded and ends on the target for every rank', () => {
    for (const rank of knownToNameless) {
      const target = redactionCutForRank(rank);
      const seq = revealCutSequence(target);
      expect(seq[0]).toBe(Math.min(...redactionCuts));
      expect(seq[seq.length - 1]).toBe(target);
      for (let i = 1; i < seq.length; i += 1) {
        expect(seq[i]).toBeGreaterThan(seq[i - 1]);
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
