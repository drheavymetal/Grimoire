// The reveal gate for The Rite (DESIGN 3.1, 5, 7). Pure and portable: the media
// query is read in the platform layer and the boolean is handed in, so this stays
// DOM-free and a test can exercise both branches without a browser.

// The reveal is a photographic develop: the name resolves over this long (DESIGN 3.1).
// It lands on the BASE Redaction cut, never a rank-driven corrosion cut — rank is null
// across the corpus, so choosing a cut by rank would render a lie (CLAUDE.md, D14/Q1).
export const REVEAL_DURATION_MS = 600;

// Whether to run the 600 ms develop animation. With prefers-reduced-motion the name is
// shown resolved immediately, with no animation (DESIGN 5/7 — accessibility floor).
export function shouldAnimateReveal(prefersReducedMotion: boolean): boolean {
  return !prefersReducedMotion;
}
