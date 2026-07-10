import { useEffect, useState } from 'react';
import { REVEAL_DURATION_MS, shouldAnimateReveal } from '../../core/domain/reveal';
import { prefersReducedMotion } from '../../platform/motion.web';

// The reveal (DESIGN 3.1): the band name emerges like a photograph in the developer —
// blurred and faint, resolving to crisp over 600 ms. It lands on the BASE Redaction face,
// NOT a rank-driven corrosion cut: rank is null across the corpus, so a cut chosen by rank
// would render a lie (CLAUDE.md, D14/Q1). The develop is driven by React state, never by a
// standalone CSS animation coupled to the DOM from core (D12): core owns the gate and the
// timing constant; this component only paints.
//
// prefers-reduced-motion (read in platform/) shows the name resolved immediately, with no
// animation (DESIGN 5/7). The gate is a pure function in core so it is tested without a browser.
export function RevealName({ name }: { name: string }) {
  const animate = shouldAnimateReveal(prefersReducedMotion());
  const [resolved, setResolved] = useState(!animate);

  useEffect(() => {
    if (!animate) {
      return;
    }

    // Start in the developing state, then flip on the next tick so the CSS transition runs.
    const handle = setTimeout(() => setResolved(true), 20);
    return () => clearTimeout(handle);
  }, [animate]);

  return (
    <span
      className="font-display text-5xl text-strong"
      style={{
        display: 'inline-block',
        transitionProperty: 'filter, opacity',
        transitionTimingFunction: 'ease-out',
        transitionDuration: `${REVEAL_DURATION_MS}ms`,
        filter: resolved ? 'blur(0) contrast(1)' : 'blur(10px) contrast(0.35)',
        opacity: resolved ? 1 : 0.12,
      }}
    >
      {name}
    </span>
  );
}
