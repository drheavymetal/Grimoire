import { useState, type ReactNode } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import type { RiteScope, ThemeKind } from '../core/domain/types';

// A clickable chip (a genre tag, a real lyrical theme, or a C21 mined theme) that opens a tiny
// two-action menu: "Invocar a ciegas" scopes a blind rite to this needle, "Ver todas" opens the
// NAMED browse grid under it. The blind/named split is the whole point — the rite narrows the pool
// but stays blind (invariant of the app's thesis), while browse is the explicit "see all" door.
//
// This lives in ui/ on purpose: it touches the DOM (a popover) and the router, which core/ must not
// (invariant 6). The chip's visual content is passed as children, so one component serves all three
// chip kinds — plain text tags and text+count theme badges alike.

// Where "Ver todas" points: a tag grid, or a theme grid (which also carries its kind).
export type ChipBrowse =
  | { kind: 'tag'; needle: string }
  | { kind: 'theme'; themeKey: string; themeKind: ThemeKind };

export function ChipMenu({
  children,
  rite,
  browse,
}: {
  children: ReactNode;
  rite: RiteScope;
  browse: ChipBrowse;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);

  const itemClass =
    'block w-full px-3 py-1.5 text-left font-mono text-xs text-strong no-underline hover:bg-accent hover:text-bg';

  return (
    <div className="relative inline-block">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        aria-haspopup="menu"
        className="inline-flex items-baseline gap-1.5 border border-line px-2 py-1 font-mono text-xs text-strong hover:border-accent hover:text-accent"
      >
        {children}
      </button>

      {open ? (
        <>
          {/* An invisible backdrop closes the menu on any outside click. */}
          <button
            type="button"
            aria-hidden="true"
            tabIndex={-1}
            onClick={() => setOpen(false)}
            className="fixed inset-0 z-10 cursor-default"
          />
          <div
            role="menu"
            className="absolute left-0 top-full z-20 mt-1 min-w-[11rem] border border-line bg-panel p-1 shadow-lg"
          >
            <Link
              to="/rite"
              search={rite}
              role="menuitem"
              onClick={() => setOpen(false)}
              className={itemClass}
            >
              {t('chipMenu.summonBlind')}
            </Link>
            {browse.kind === 'tag' ? (
              <Link
                to="/browse/tag/$needle"
                params={{ needle: browse.needle }}
                role="menuitem"
                onClick={() => setOpen(false)}
                className={itemClass}
              >
                {t('chipMenu.seeAll')}
              </Link>
            ) : (
              <Link
                to="/browse/theme/$key"
                params={{ key: browse.themeKey }}
                search={{ kind: browse.themeKind }}
                role="menuitem"
                onClick={() => setOpen(false)}
                className={itemClass}
              >
                {t('chipMenu.seeAll')}
              </Link>
            )}
          </div>
        </>
      ) : null}
    </div>
  );
}
