import type { ReactNode } from 'react';

// A section kicker for the hub pages (Explore, Mirror, Lineage…). A short sulphur
// tick rule sets each section off as an authored chapter instead of a stack of identical headings —
// the single-accent editorial device of the reference. It renders as a fragment on purpose: the h2
// stays a DIRECT child of its section container, so the e2e suite's heading-scoped queries
// (`getByRole('heading', …).locator('..')`) still resolve to the section, not to a wrapper. Pure
// presentation — no data, no DOM access beyond the markup.
export function SectionHead({ title, hint }: { title: string; hint?: ReactNode }) {
  return (
    <>
      <span aria-hidden="true" className="block h-px w-8 bg-accent" />
      <h2 className="mt-3 font-display text-2xl text-strong">{title}</h2>
      {hint !== undefined ? <p className="mt-1 max-w-prose font-mono text-xs text-muted">{hint}</p> : null}
    </>
  );
}
