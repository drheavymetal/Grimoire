import type { Rank } from './types';

// Redaction ships graded "generational loss" cuts as separate @fontsource packages. The number is
// how DEGRADED the face is, NOT how crisp: redaction-10 is the cleanest, elegant serif and
// redaction-100 is the most eroded — a blocky, near-illegible photocopy-of-a-photocopy. This was
// verified by rendering all six faces (scratchpad/redaction-preview.png): cut 10 reads clean, cut
// 100 is a pixelated mess. The earlier code AND DESIGN.md §3 had this backwards ("100 nítida"),
// which made common bands render as the ugly blocky face and rare ones render clean — corrosion
// running with popularity instead of against it. Corrected here to what the fonts actually do.
export const redactionCuts = [10, 20, 35, 50, 70, 100] as const;
export type RedactionCut = (typeof redactionCuts)[number];

// The cleanest face, used for a name whose rank is unknown. Unknown is not rare, so it renders
// crisp, never corroded — the same rule the engine uses for null listeners (D35). Most of the
// corpus is unranked until the Last.fm pass fills in, so this is what the app mostly shows: clean.
export const BASE_REDACTION_CUT: RedactionCut = 10;

// The most corroded face a static name is ever shown in. Rarer than this the glyphs stop being
// legible (cut 100 is reserved for the transient first frame of the reveal only), so the rarity
// gradient is capped here — corroded, but still readable, which is the whole of Pedro's note.
const MAX_STATIC_CORROSION: RedactionCut = 70;

// Pure mapping from rarity to corrosion, the signature of Q1 (D14/D38). The typography IS the datum:
// rarer bands corrode MORE, so the cut number RISES with rarity — Known is the clean cut 10, Nameless
// the heavily-eroded (but still legible) cut 70. A band whose rank is unknown renders at the clean
// base, never corroded — unknown is not rare (D35). Wired: rank is populated across the corpus, so a
// cut chosen by rank renders the truth.
export function redactionCutForRank(rank: Rank | null): RedactionCut {
  if (rank === null) {
    return BASE_REDACTION_CUT;
  }

  switch (rank) {
    case 'Known':
      return 10;
    case 'Obscure':
      return 20;
    case 'Hidden':
      return 35;
    case 'Forgotten':
      return 50;
    case 'Nameless':
      return MAX_STATIC_CORROSION;
  }
}

// The CSS font-family stack for a corrosion cut. Each graded cut ships as its own @fontsource
// package with family name 'Redaction <N>' (verified: 'Redaction 10' … 'Redaction 100'); the base
// 'Redaction' family and a serif keep it legible if a graded face fails to load. Pure string
// building, no DOM — the ui layer feeds it to an inline style.
export function redactionFontFamily(cut: RedactionCut): string {
  return `'Redaction ${cut}', 'Redaction', Georgia, serif`;
}

// The reveal of The Rite (DESIGN §3.1): the name develops like a photograph — it emerges in the most
// corroded face (cut 100) and resolves, cut by cut, DOWN to the face its rank earns. A Known walks all
// the way to clean (10); a Nameless resolves only to its capped, still-eroded cut (70) and no further —
// it never fully clears, because it never fully leaves the dark. Pure and ordered so the animation is a
// state-driven walk (D12), testable without a browser. Descending: index 0 is the most corroded frame.
export function revealCutSequence(targetCut: RedactionCut): RedactionCut[] {
  return redactionCuts.filter((cut) => cut >= targetCut).reverse();
}
