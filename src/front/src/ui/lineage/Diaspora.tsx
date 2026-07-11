import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useDiaspora } from '../../core/hooks/useLineage';

// B11 — Diaspora: a band breaks up, and its members scatter. For each member who left (a known
// end date) we list the bands they joined afterward, dated from the graph. Nothing is invented:
// a move only shows when both dates support it. Empty when nobody left for a dated later band.

export function Diaspora({ artistId }: { artistId: string }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useDiaspora(artistId);

  if (isLoading) {
    return (
      <section className="mt-10">
        <h2 className="font-display text-2xl text-strong">{t('lineage.diasporaTitle')}</h2>
        <p className="mt-3 font-mono text-sm text-muted">{t('lineage.loading')}</p>
      </section>
    );
  }

  if (isError || data === undefined) {
    return (
      <section className="mt-10">
        <h2 className="font-display text-2xl text-strong">{t('lineage.diasporaTitle')}</h2>
        <p className="mt-3 font-mono text-sm text-danger">{t('lineage.error')}</p>
      </section>
    );
  }

  return (
    <section className="mt-10">
      <h2 className="font-display text-2xl text-strong">{t('lineage.diasporaTitle')}</h2>
      <p className="mt-1 font-mono text-xs text-muted">{t('lineage.diasporaHint')}</p>

      {data.members.length === 0 ? (
        <div className="mt-3 border border-line border-dashed p-6 text-center">
          <p className="font-body text-sm text-muted">{t('lineage.diasporaEmpty')}</p>
        </div>
      ) : (
        <ul className="mt-4 space-y-4">
          {data.members.map((member) => (
            <li key={member.memberId} className="border-b border-line pb-3">
              <div className="flex items-baseline justify-between gap-3">
                <Link
                  to="/artist/$artistId"
                  params={{ artistId: member.memberId }}
                  className="font-display text-lg text-strong no-underline hover:text-accent"
                >
                  {member.memberName}
                </Link>
                <span className="shrink-0 font-mono text-xs text-muted">
                  {member.leftDate !== null
                    ? t('lineage.leftIn', { year: member.leftDate.slice(0, 4) })
                    : ''}
                </span>
              </div>
              <ul className="mt-2 space-y-1">
                {member.destinations.map((dest) => (
                  <li key={dest.bandId} className="flex items-baseline gap-2 pl-4 font-body text-sm">
                    <span className="text-accent">→</span>
                    <Link
                      to="/artist/$artistId"
                      params={{ artistId: dest.bandId }}
                      className="text-strong no-underline hover:text-accent"
                    >
                      {dest.bandName}
                    </Link>
                    {dest.joinedYear !== null ? (
                      <span className="font-mono text-xs text-muted">{dest.joinedYear.slice(0, 4)}</span>
                    ) : null}
                  </li>
                ))}
              </ul>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
