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
export const BASE_REDACTION_CUT: RedactionCut = 100;

// Pure mapping from rarity to corrosion, the signature of Q1 (D14/D38, ratified by Pedro for the
// autonomous build). The typography IS the datum: rarer bands corrode more — the crisp end (100)
// is Known, the eroded end (10) is Nameless. A band whose rank is unknown renders at the crisp
// base (100), never corroded — unknown is not rare (the same rule the engine uses for null
// listeners, D35). This is now WIRED: rank is populated across most of the corpus, so a cut chosen
// by rank renders the truth, not a lie.
export function redactionCutForRank(rank: Rank | null): RedactionCut {
  if (rank === null) {
    return BASE_REDACTION_CUT;
  }

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

// The CSS font-family stack for a corrosion cut. Each graded cut ships as its own @fontsource
// package with family name 'Redaction <N>' (verified: 'Redaction 10' … 'Redaction 100'); the base
// 'Redaction' family and a serif keep it legible if a graded face fails to load. Pure string
// building, no DOM — the ui layer feeds it to an inline style.
export function redactionFontFamily(cut: RedactionCut): string {
  return `'Redaction ${cut}', 'Redaction', Georgia, serif`;
}

// The reveal of The Rite (DESIGN §3.1): the name emerges corroded and resolves toward the cut its
// rank earns. This is the ordered sequence of cuts the develop steps through — from the most
// corroded face (10) up to and including the target. A Known (target 100) walks the whole ladder to
// crisp; a Nameless (target 10) is a single-frame [10] that never resolves. Pure and ordered so the
// animation is a state-driven walk (D12), testable without a browser.
export function revealCutSequence(targetCut: RedactionCut): RedactionCut[] {
  return redactionCuts.filter((cut) => cut <= targetCut);
}
