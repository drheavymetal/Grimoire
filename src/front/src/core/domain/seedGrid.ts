import type { SeedCandidate } from './types';

// The cold-start grid grows, it never reshuffles.
//
// Re-ranking the whole grid around the picks reads well on paper and is miserable in the hand: pick a
// band in the seventh row and every row above it changes, so the eye has to start again from the top
// after each click. Instead the grid is a stable list into which a pick's neighbours are spliced
// directly beneath it. Everything already read stays exactly where it was read.

/**
 * Inserts `related` immediately below the band at `index`, dropping any band already in `grid`
 * (a neighbour of Judas Priest is very likely on screen already — showing it twice would be a lie
 * about how big the neighbourhood is). Returns a new array; the input is not touched.
 */
export function insertRelatedBelow(
  grid: readonly SeedCandidate[],
  index: number,
  related: readonly SeedCandidate[],
): SeedCandidate[] {
  if (index < 0 || index >= grid.length) {
    return [...grid];
  }

  const known = new Set(grid.map((band) => band.id));
  const fresh: SeedCandidate[] = [];

  for (const band of related) {
    if (!known.has(band.id)) {
      known.add(band.id);
      fresh.push(band);
    }
  }

  if (fresh.length === 0) {
    return [...grid];
  }

  return [...grid.slice(0, index + 1), ...fresh, ...grid.slice(index + 1)];
}
