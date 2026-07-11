import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useMemoriam } from '../../core/hooks/useMemoriam';
import type { MemoriamEntry } from '../../core/domain/types';
import { PageHeader } from '../PageHeader';

// C12 — In Memoriam. The musicians in the grimoire who have died, in chronological order, with
// their bands. Real data off death_date/death_place (Wikidata P570/P20); the tone is deliberately
// plain — a record, not a spectacle. A quiet, designed empty state when nothing is on record.

export function MemoriamPage() {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useMemoriam();
  const entries = data ?? [];

  return (
    <section>
      {/* No flyer grain here: In Memoriam is a reading surface, kept quiet on purpose (D14 reserves
          the halftone for surfaces of impact — this is not one). */}
      <PageHeader
        eyebrow={t('memoriam.eyebrow')}
        title={t('memoriam.heading')}
        lead={<p className="font-body text-sm text-muted">{t('memoriam.intro')}</p>}
        flyer={false}
      />

      {isLoading ? <p className="mt-6 font-mono text-sm text-muted">{t('memoriam.loading')}</p> : null}
      {isError ? <p className="mt-6 font-mono text-sm text-danger">{t('memoriam.error')}</p> : null}

      {!isLoading && !isError && entries.length === 0 ? (
        <div className="mt-6 border border-line border-dashed p-8 text-center">
          <p className="font-body text-sm text-muted">{t('memoriam.empty')}</p>
        </div>
      ) : null}

      {/* A chronology spine: a faint rule down the years, a sulphur node per entry. It reads as a
          timeline without ornament — a record, not a spectacle. */}
      {entries.length > 0 ? (
        <ol className="mt-10 ml-2 space-y-10 border-l border-line pl-7">
          {entries.map((entry) => (
            <MemoriamRow key={entry.id} entry={entry} />
          ))}
        </ol>
      ) : null}
    </section>
  );
}

function MemoriamRow({ entry }: { entry: MemoriamEntry }) {
  const { t } = useTranslation();
  const year = entry.deathDate.slice(0, 4);

  return (
    <li className="relative">
      <span
        aria-hidden="true"
        className="absolute left-[-1.75rem] top-2 h-2 w-2 -translate-x-1/2 rounded-full bg-accent ring-4 ring-bg"
      />
      <div className="font-display text-2xl leading-none text-accent">{year}</div>
      <div className="mt-1">
        <h2 className="font-display text-2xl text-strong">{entry.name}</h2>
        <p className="mt-0.5 font-mono text-xs text-muted">
          {entry.deathDate}
          {entry.deathPlace !== null ? ` · ${entry.deathPlace}` : ''}
        </p>
        {entry.bands.length > 0 ? (
          <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1">
            {entry.bands.map((band) => (
              <li key={band.id}>
                <Link
                  to="/artist/$artistId"
                  params={{ artistId: band.id }}
                  className="font-body text-sm text-strong no-underline hover:text-accent"
                >
                  {band.name}
                </Link>
              </li>
            ))}
          </ul>
        ) : (
          <p className="mt-2 font-mono text-xs text-muted">{t('memoriam.noBands')}</p>
        )}
      </div>
    </li>
  );
}
