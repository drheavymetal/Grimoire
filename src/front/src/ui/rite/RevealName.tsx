import { useEffect, useMemo, useState } from 'react';
import {
  redactionCutForRank,
  redactionFontFamily,
  revealCutSequence,
} from '../../core/domain/redaction';
import { REVEAL_DURATION_MS, shouldAnimateReveal } from '../../core/domain/reveal';
import type { Rank } from '../../core/domain/types';
import { prefersReducedMotion } from '../../platform/motion.web';

// The reveal (DESIGN §3.1): the band name develops like a photograph — it emerges in the most
// corroded Redaction face (cut 100) and resolves, cut by cut, DOWN to the face its RANK earns, over
// 600 ms. A Known walks all the way to clean (10); a Nameless resolves only to its capped, still-
// eroded cut (70) and never fully clears; an unknown rank lands clean, because unknown is not rare
// (D35). This wires Q1's signature into the reveal with the real graded faces (D14/D38), replacing
// the earlier blur/contrast stand-in. The corrosion is only ever the band name — the datum — never
// the app mark (D27).
//
// The develop is a state-driven walk through a pure, ordered sequence from core (D12): core owns the
// sequence and the timing; this component only steps an index and paints. prefers-reduced-motion
// (read in platform/) shows the final cut at once, no animation (DESIGN §5/§7).
export function RevealName({ name, rank }: { name: string; rank: Rank | null }) {
  const targetCut = redactionCutForRank(rank);
  const sequence = useMemo(() => revealCutSequence(targetCut), [targetCut]);
  const animate = shouldAnimateReveal(prefersReducedMotion());

  // Start at the most corroded frame when animating, or straight at the target when not.
  const [step, setStep] = useState(() => (animate ? 0 : sequence.length - 1));

  useEffect(() => {
    if (!animate || sequence.length <= 1) {
      setStep(sequence.length - 1);
      return;
    }

    setStep(0);
    const perStep = REVEAL_DURATION_MS / (sequence.length - 1);
    const timers = sequence.map((_, i) => setTimeout(() => setStep(i), Math.round(perStep * i)));

    return () => {
      for (const timer of timers) {
        clearTimeout(timer);
      }
    };
  }, [animate, sequence]);

  const cut = sequence[Math.min(step, sequence.length - 1)];
  const resolved = step >= sequence.length - 1;

  return (
    <span
      className="text-5xl text-strong"
      style={{
        display: 'inline-block',
        fontFamily: redactionFontFamily(cut),
        transitionProperty: 'opacity',
        transitionTimingFunction: 'ease-out',
        transitionDuration: `${REVEAL_DURATION_MS}ms`,
        opacity: resolved ? 1 : 0.55,
      }}
    >
      {name}
    </span>
  );
}
