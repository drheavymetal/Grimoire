import type { Rank } from './types';

// Redaction ships graded corrosion cuts as separate @fontsource packages, from
// redaction-10 (crispest) to redaction-100 (most corroded). Verified against
// fontsource 5.2.5 in docs/progress/skeleton.md.
export const redactionCuts = [10, 20, 35, 50, 70, 100] as const;
export type RedactionCut = (typeof redactionCuts)[number];

// The cut the UI uses today. Rank is null across the whole corpus in movement I
// (listeners were never gathered — no Last.fm key), so components render the base
// Redaction face and never pick a cut from a rank that does not exist.
export const BASE_REDACTION_CUT: RedactionCut = 10;

// Pure mapping from rarity to corrosion, kept for the future rank-driven signature
// (D14 / open question Q1, unratified). Deliberately NOT wired into any component:
// choosing a cut by rank while rank is null would render a lie (CLAUDE.md). Rarer
// bands corrode more — the type is the datum.
export function redactionCutForRank(rank: Rank): RedactionCut {
  switch (rank) {
    case 'Known':
      return 10;
    case 'Obscure':
      return 20;
    case 'Hidden':
      return 50;
    case 'Forgotten':
      return 70;
    case 'Nameless':
      return 100;
  }
}
