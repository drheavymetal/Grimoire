import type { ReactNode } from 'react';

// A page masthead in the app's voice (DESIGN §1/§3, D14/D27). A Courier eyebrow struck in sulphur
// gives each screen its own chapter kicker, then the display title, then an optional lead. On the
// flyer surface the light-mode halftone grain shows through (the photocopied flyer of D14); dark
// mode stays the clean void (the cassette). This is a shared frame, not a generic template: the
// eyebrow is what keeps each page from reading as tokens inherited without care.
//
// The title renders as a plain heading so its accessible name stays exactly the string passed — the
// e2e suite finds every page by its heading name, and the eyebrow must never leak into it.
export function PageHeader({
  eyebrow,
  title,
  lead,
  aside,
  flyer = true,
}: {
  eyebrow?: string;
  title: string;
  lead?: ReactNode;
  aside?: ReactNode;
  flyer?: boolean;
}) {
  return (
    <header
      className={`border-b border-line ${flyer ? 'flyer -mx-5 -mt-10 px-5 pb-7 pt-10' : 'pb-6'}`}
    >
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          {eyebrow !== undefined ? (
            <p className="font-mono text-[0.7rem] uppercase tracking-[0.28em] text-accent">{eyebrow}</p>
          ) : null}
          <h1 className="mt-2 font-display text-4xl leading-[0.95] text-strong sm:text-5xl">{title}</h1>
        </div>
        {aside !== undefined ? <div className="shrink-0 pt-1">{aside}</div> : null}
      </div>
      {lead !== undefined ? <div className="mt-3 max-w-prose">{lead}</div> : null}
    </header>
  );
}
