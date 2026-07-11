import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useMemoriam } from '../../core/hooks/useMemoriam';
import type { MemoriamEntry } from '../../core/domain/types';

// C12 — In Memoriam. The musicians in the grimoire who have died, in chronological order, with
// their bands. Real data off death_date/death_place (Wikidata P570/P20); the tone is deliberately
// plain — a record, not a spectacle. A quiet, designed empty state when nothing is on record.

export function MemoriamPage() {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useMemoriam();
  const entries = data ?? [];

  return (
    <section>
      <div className="border-b border-line pb-6">
        <h1 className="font-display text-4xl text-strong">{t('memoriam.heading')}</h1>
        <p className="mt-2 max-w-prose font-body text-sm text-muted">{t('memoriam.intro')}</p>
      </div>

      {isLoading ? <p className="mt-6 font-mono text-sm text-muted">{t('memoriam.loading')}</p> : null}
      {isError ? <p className="mt-6 font-mono text-sm text-danger">{t('memoriam.error')}</p> : null}

      {!isLoading && !isError && entries.length === 0 ? (
        <div className="mt-6 border border-line border-dashed p-8 text-center">
          <p className="font-body text-sm text-muted">{t('memoriam.empty')}</p>
        </div>
      ) : null}

      {entries.length > 0 ? (
        <ol className="mt-8 space-y-8">
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
    <li className="grid grid-cols-[4rem_1fr] gap-4">
      <div className="font-display text-2xl text-accent">{year}</div>
      <div>
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
