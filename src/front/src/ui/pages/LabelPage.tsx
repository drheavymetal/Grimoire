import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useLabel } from '../../core/hooks/useLabels';
import { Cover } from '../Cover';

// B21 — a label's page: its roster. Each release links through to the band it belongs to (the door).
// 404 → not found; an empty roster is a designed empty state.

export function LabelPage({ labelId }: { labelId: string }) {
  const { t } = useTranslation();
  const { data, isLoading, isError, error } = useLabel(labelId);

  const notFound = isError && error instanceof Error && 'status' in error && (error as { status: number }).status === 404;

  if (isLoading) {
    return <p className="font-mono text-sm text-muted">{t('label.loading')}</p>;
  }

  if (notFound) {
    return (
      <div>
        <p className="font-body text-sm text-muted">{t('label.notFound')}</p>
        <Link to="/labels" className="mt-3 inline-block font-mono text-xs uppercase text-muted hover:text-accent">
          {t('label.back')}
        </Link>
      </div>
    );
  }

  if (isError || data === undefined) {
    return <p className="font-mono text-sm text-danger">{t('label.error')}</p>;
  }

  return (
    <section>
      <Link to="/labels" className="font-mono text-xs uppercase text-muted no-underline hover:text-accent">
        {t('label.back')}
      </Link>

      <div className="mt-3 flyer -mx-5 border-y border-line px-5 py-6">
        <p className="font-mono text-[0.7rem] uppercase tracking-[0.28em] text-accent">{t('label.eyebrow')}</p>
        <h1 className="mt-2 font-display text-4xl leading-[0.95] text-strong sm:text-5xl">{data.name}</h1>
        <p className="mt-2 font-mono text-xs uppercase text-muted">
          {data.country ?? t('label.countryUnknown')} · {t('labels.releaseCount', { count: data.releases.length })}
        </p>
      </div>

      {data.releases.length === 0 ? (
        <div className="mt-6 border border-line border-dashed p-8 text-center">
          <p className="font-body text-sm text-muted">{t('label.empty')}</p>
        </div>
      ) : (
        <ul className="mt-6 divide-y divide-line border-y border-line">
          {data.releases.map((release) => (
            <li key={release.id} className="flex items-center gap-3 py-3">
              <Cover mbid={release.mbid} title={release.title} />
              <div className="min-w-0 flex-1">
                <p className="truncate font-body text-strong">{release.title}</p>
                <p className="font-mono text-xs text-muted">
                  {t(`releaseType.${release.type}`)}
                  {release.releaseDate ? ` · ${release.releaseDate.slice(0, 4)}` : ''}
                </p>
              </div>
              <Link
                to="/artist/$artistId"
                params={{ artistId: release.artistId }}
                className="shrink-0 font-display text-lg text-strong no-underline hover:text-accent"
              >
                {release.artistName}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
