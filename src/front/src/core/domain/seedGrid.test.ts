import { describe, expect, it } from 'vitest';
import { insertRelatedBelow } from './seedGrid';
import type { SeedCandidate } from './types';

const band = (id: string): SeedCandidate => ({ id, name: id, country: null, formedYear: null });

describe('insertRelatedBelow', () => {
  it('puts the related bands right under the picked one', () => {
    const grid = [band('a'), band('b'), band('c')];

    const grown = insertRelatedBelow(grid, 1, [band('b1'), band('b2')]);

    expect(grown.map((x) => x.id)).toEqual(['a', 'b', 'b1', 'b2', 'c']);
  });

  it('leaves everything above the pick untouched — the whole point', () => {
    const grid = Array.from({ length: 20 }, (_, i) => band(`g${i}`));

    const grown = insertRelatedBelow(grid, 12, [band('n1'), band('n2')]);

    // Rows 0..12 are byte-for-byte where they were: the eye does not restart at the top.
    expect(grown.slice(0, 13)).toEqual(grid.slice(0, 13));
    expect(grown[13].id).toBe('n1');
  });

  it('drops a related band already on the grid instead of showing it twice', () => {
    const grid = [band('a'), band('b'), band('c')];

    const grown = insertRelatedBelow(grid, 0, [band('c'), band('new')]);

    expect(grown.map((x) => x.id)).toEqual(['a', 'new', 'b', 'c']);
  });

  it('is a no-op when nothing is fresh, or the index is not on the grid', () => {
    const grid = [band('a'), band('b')];

    expect(insertRelatedBelow(grid, 0, [band('b')]).map((x) => x.id)).toEqual(['a', 'b']);
    expect(insertRelatedBelow(grid, 9, [band('z')]).map((x) => x.id)).toEqual(['a', 'b']);
    expect(insertRelatedBelow(grid, -1, [band('z')]).map((x) => x.id)).toEqual(['a', 'b']);
  });
});
