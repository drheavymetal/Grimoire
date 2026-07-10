// Reads the reduced-motion preference (DESIGN 5/7). Isolated in platform/ so the reveal
// gate in core/ stays a pure function of a boolean and can be tested without a browser.
export function prefersReducedMotion(): boolean {
  try {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  } catch {
    // No matchMedia (SSR, older engines): default to full motion.
    return false;
  }
}
