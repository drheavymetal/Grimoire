import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useMemberBands } from '../../core/hooks/useLineage';

// B3 — "bands where X played": every band a musician was a member of, with their stint and
// instruments in each. Shown on a person's page (reached by searching that person). Complements
// the Gantt with a plain, linkable list. Empty when no memberships are on record.

export function MemberBands({ personId, enabled }: { personId: string; enabled: boolean }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useMemberBands(personId, enabled);

  if (!enabled) {
    return null;
  }

  if (isLoading) {
    return (
      <section className="mt-10">
        <h2 className="font-display text-2xl text-strong">{t('lineage.bandsTitle')}</h2>
        <p className="mt-3 font-mono text-sm text-muted">{t('lineage.loading')}</p>
      </section>
    );
  }

  if (isError || data === undefined) {
    return (
      <section className="mt-10">
        <h2 className="font-display text-2xl text-strong">{t('lineage.bandsTitle')}</h2>
        <p className="mt-3 font-mono text-sm text-danger">{t('lineage.error')}</p>
      </section>
    );
  }

  return (
    <section className="mt-10">
      <h2 className="font-display text-2xl text-strong">{t('lineage.bandsTitle')}</h2>

      {data.bands.length === 0 ? (
        <div className="mt-3 border border-line border-dashed p-6 text-center">
          <p className="font-body text-sm text-muted">{t('lineage.bandsEmpty')}</p>
        </div>
      ) : (
        <ul className="mt-4 divide-y divide-line border-y border-line">
          {data.bands.map((band) => (
            <li key={`${band.bandId}-${band.beginDate ?? '?'}`} className="py-3">
              <div className="flex items-baseline justify-between gap-3">
                <Link
                  to="/artist/$artistId"
                  params={{ artistId: band.bandId }}
                  className="font-display text-lg text-strong no-underline hover:text-accent"
                >
                  {band.bandName}
                </Link>
                <span className="shrink-0 font-mono text-xs text-muted">
                  {formatStint(band.beginDate, band.endDate)}
                </span>
              </div>
              {band.instruments.length > 0 ? (
                <p className="mt-1 font-mono text-xs text-muted">{band.instruments.join(', ')}</p>
              ) : null}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function formatStint(begin: string | null, end: string | null): string {
  const from = begin !== null ? begin.slice(0, 4) : '?';
  const to = end !== null ? end.slice(0, 4) : '';
  return `${from}–${to}`;
}
