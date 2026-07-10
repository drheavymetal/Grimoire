import type { Rank, ReleaseType } from './types';

// Pure, portable rank derivation, mirroring the backend RankCalculator thresholds
// (SPEC section 6 / DECISIONS D3). Returns null when listeners are unknown, so the UI
// never invents a rank from missing data.
export function rankFromListeners(listeners: number | null): Rank | null {
  if (listeners === null) {
    return null;
  }

  if (listeners < 500) {
    return 'Nameless';
  }

  if (listeners < 5_000) {
    return 'Forgotten';
  }

  if (listeners < 50_000) {
    return 'Hidden';
  }

  // SPEC: "> 500 000 → Known" is strict, so 500 000 itself is Obscure.
  if (listeners <= 500_000) {
    return 'Obscure';
  }

  return 'Known';
}

// Order in which release types are grouped on the artist page. The demo is a
// first-class release and sits high, not hidden under a toggle.
export const releaseTypeOrder: ReleaseType[] = ['Album', 'Ep', 'Demo', 'Split', 'Live', 'Compilation'];
