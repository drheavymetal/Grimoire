import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

// A section of a hub page that folds away. The header keeps SectionHead's editorial furniture — the
// sulphur tick rule, the display h2, the mono hint — but the title becomes a disclosure button, so
// this is its own component rather than a branch inside SectionHead: one is a static kicker, the
// other is a control.
//
// Two contracts it must not break:
//
//  * The h2 stays a DIRECT child of the section container, exactly as SectionHead's comment demands,
//    because the e2e suite scopes to a section with `getByRole('heading', …).locator('..')`. The
//    button lives INSIDE the h2 (the WAI-ARIA disclosure pattern), which keeps both the heading role
//    and the section-is-the-parent assumption true.
//  * The show/hide word is aria-hidden: `aria-expanded` already tells a screen reader the state, and
//    letting the word into the accessible name would make it read "Wall of covers Hide".
//
// Folding renders `children` away entirely. The caller keeps its own hook alive and passes `open`
// down as the query's `enabled`, so a folded section costs nothing while its local state (a picked
// band, a chosen pole) survives the fold.
export function CollapsibleSection({
  title,
  hint,
  open,
  onToggle,
  children,
}: {
  title: string;
  hint?: ReactNode;
  open: boolean;
  onToggle: () => void;
  children: ReactNode;
}) {
  const { t } = useTranslation();

  return (
    <div className="mt-12">
      <span aria-hidden="true" className="block h-px w-8 bg-accent" />
      <h2 className="mt-3 font-display text-2xl text-strong">
        <button
          type="button"
          onClick={onToggle}
          aria-expanded={open}
          className="flex w-full items-baseline justify-between gap-3 text-left hover:text-accent"
        >
          {title}
          <span aria-hidden="true" className="shrink-0 font-mono text-xs uppercase text-muted">
            {open ? t('explore.hide') : t('explore.show')}
          </span>
        </button>
      </h2>
      {hint !== undefined ? <p className="mt-1 max-w-prose font-mono text-xs text-muted">{hint}</p> : null}

      {open ? children : null}
    </div>
  );
}
