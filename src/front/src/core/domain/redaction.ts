import type { Rank } from './types';

// Redaction ships graded corrosion cuts as separate @fontsource packages. The
// number is legibility, not corrosion: redaction-100 is the crispest cut and
// redaction-10 is the most corroded (verified empirically — the 100 face renders
// clean, the 10 face is eroded; the woff2 for 10 is also by far the heaviest,
// carrying the broken-edge detail). This matches DESIGN.md §3 ("10 casi ilegible
// … 100 nítida"); the earlier note in skeleton.md had the direction backwards.
export const redactionCuts = [10, 20, 35, 50, 70, 100] as const;
export type RedactionCut = (typeof redactionCuts)[number];

// The default face for a name whose rank is unknown. Unknown is not rare, so it
// renders crisp, never corroded — the same rule the engine uses for null listeners.
// (Components render the base 'Redaction' family via --font-display; this constant
// exists for the future rank-driven signature, not wired yet — Q1 unratified.)
export const BASE_REDACTION_CUT: RedactionCut = 100;

// Pure mapping from rarity to corrosion, kept for the future rank-driven signature
// (D14 / open question Q1, unratified). Deliberately NOT wired into any component:
// choosing a cut by rank while rank is null would render a lie (CLAUDE.md). Rarer
// bands corrode more — the crisp end (100) is Known, the eroded end (10) is Nameless.
export function redactionCutForRank(rank: Rank): RedactionCut {
  switch (rank) {
    case 'Known':
      return 100;
    case 'Obscure':
      return 70;
    case 'Hidden':
      return 50;
    case 'Forgotten':
      return 35;
    case 'Nameless':
      return 10;
  }
}
