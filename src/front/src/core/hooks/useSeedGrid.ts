import { useEffect, useState } from 'react';
import { useRelatedSeeds, useSeedCandidates } from './useSeedCandidates';
import { insertRelatedBelow } from '../domain/seedGrid';
import type { SeedCandidate } from '../domain/types';

// The API seeds a taste from at most twenty bands (MaxSeedArtists).
export const MAX_PICKS = 20;

// The picker state shared by BOTH the sign-up cold start (ColdStart) and the profile's "reselect
// your bands" panel (ProfilePage). It is NOT blind on purpose: the user is choosing seeds they
// already know, exactly as at sign-up — reusing it here is correct. DOM-free: it holds only the
// user's trail through the catalogue, so it lives in core/ and the UI renders it (see SeedPicker).
//
// The grid GROWS, it never reshuffles: picking a band unfolds its neighbours directly beneath it and
// leaves every row above exactly where it was. See core/domain/seedGrid.ts for why.
export function useSeedGrid(enabled: boolean) {
  const { data, isLoading, isError } = useSeedCandidates(enabled);
  const related = useRelatedSeeds();

  // The grid the user is actually looking at: the fetched one, then whatever their picks unfolded
  // into it. Held here because it is the user's own trail through the catalogue, not server state.
  const [grid, setGrid] = useState<SeedCandidate[]>([]);
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [expanding, setExpanding] = useState<string | null>(null);

  // Seed the grid from the fetched candidates, once. It is never re-seeded from a later fetch: that
  // would throw away the rows the user has already unfolded and read.
  useEffect(() => {
    if (data !== undefined) {
      setGrid((current) => (current.length === 0 ? data : current));
    }
  }, [data]);

  const full = picked.size >= MAX_PICKS;

  async function toggle(band: SeedCandidate, index: number) {
    if (picked.has(band.id)) {
      // Unpicking only unmarks the chip. The bands it unfolded stay: pulling them back out would
      // shift the whole grid under the user's hand, which is the very thing this screen must not do.
      setPicked((current) => {
        const next = new Set(current);
        next.delete(band.id);
        return next;
      });
      return;
    }

    if (full) {
      return;
    }

    setPicked((current) => new Set(current).add(band.id));
    setExpanding(band.id);

    try {
      const neighbours = await related.mutateAsync(band.id);
      setGrid((current) => {
        // The band may have moved if an earlier pick unfolded above it — find it again, do not
        // trust the index the click was made at.
        const at = current.findIndex((row) => row.id === band.id);
        return insertRelatedBelow(current, at === -1 ? index : at, neighbours);
      });
    } catch {
      // A neighbourhood that would not load costs the user nothing: the pick still counts, the grid
      // simply does not grow. No error state for a band they already chose.
    } finally {
      setExpanding(null);
    }
  }

  // Clear the picks (used after a successful reseed so the panel starts clean if reopened).
  function reset() {
    setPicked(new Set());
  }

  return { grid, picked, toggle, reset, expanding, isLoading, isError, full };
}
