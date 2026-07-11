// Pure decision: which body an artist page should render (movement VII, D11). No DOM, no platform
// coupling (invariant 6) — the ui/ layer switches on what this returns, and it is unit-tested
// without a browser.
//
// The rule (D11 / composer-ficha brief):
//   - an artist with works  -> the composer body (works + master–apprentice lineage, no Gantt)
//   - a Group otherwise     -> the band ficha (the Gantt is the hero)
//   - a Person otherwise    -> the member page (B10), the band ficha's person variant
//
// Works win over everything: a composer is modelled as a Person, so the works signal must be
// checked before the kind, or every composer would fall through to the member page.

import type { ArtistKind } from './types';

export type ArtistView = 'composer' | 'band' | 'member';

export interface ArtistViewInput {
  hasWorks: boolean;
  kind: ArtistKind;
}

export function resolveArtistView({ hasWorks, kind }: ArtistViewInput): ArtistView {
  if (hasWorks) {
    return 'composer';
  }

  if (kind === 'Person') {
    return 'member';
  }

  return 'band';
}
