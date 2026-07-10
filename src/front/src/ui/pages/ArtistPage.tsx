import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useArtist } from '../../core/hooks/useArtist';
import { releaseTypeOrder } from '../../core/domain/rank';
import { ApiError } from '../../core/api/client';
import type { ArtistEdge, Release, ReleaseType } from '../../core/domain/types';
import { Cover } from '../Cover';

export function ArtistPage({ artistId }: { artistId: string }) {
  const { t } = useTranslation();
  const { data, isLoading, isError, error } = useArtist(artistId);

  if (isLoading) {
    return <p className="font-mono text-sm text-muted">{t('artist.loading')}</p>;
  }

  if (isError) {
    const notFound = error instanceof ApiError && error.status === 404;
    return (
      <div>
        <p className="font-mono text-sm text-danger">
          {notFound ? t('artist.notFound') : t('artist.error')}
        </p>
        <BackLink />
      </div>
    );
  }

  if (data === undefined) {
    return null;
  }

  const grouped = groupReleases(data.releases);

  return (
    <article>
      <BackLink />
      {/* Rank is null across the whole corpus in movement I: the display uses the base
          Redaction face, never a rank-driven corrosion cut (CLAUDE.md, D14/Q1). */}
      <h1 className="mt-3 font-display text-5xl text-strong">{data.name}</h1>

      <dl className="mt-4 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 font-mono text-xs text-muted">
        <dt className="uppercase">{t('artist.origin')}</dt>
        <dd className="text-strong">{data.country ?? '—'}{data.city ? ` · ${data.city}` : ''}</dd>
        <dt className="uppercase">{t('artist.formed')}</dt>
        <dd className="text-strong">{data.formedYear ?? '—'}</dd>
        {data.dissolvedYear !== null ? (
          <>
            <dt className="uppercase">{t('artist.dissolved')}</dt>
            <dd className="text-strong">{data.dissolvedYear}</dd>
          </>
        ) : null}
        <dt className="uppercase">{t('artist.rank')}</dt>
        <dd className={data.rank !== null ? 'text-accent' : 'text-muted'}>
          {data.rank !== null ? t(`rank.${data.rank}`) : t('artist.rankUnknown')}
        </dd>
      </dl>

      <section className="mt-6">
        <h2 className="font-mono text-xs uppercase text-muted">{t('artist.tags')}</h2>
        {data.tags.length > 0 ? (
          <ul className="mt-2 flex flex-wrap gap-2">
            {data.tags.map((tag) => (
              <li key={tag} className="border border-line px-2 py-1 font-mono text-xs text-strong">
                {tag}
              </li>
            ))}
          </ul>
        ) : (
          <p className="mt-2 font-mono text-xs text-muted">{t('artist.noTags')}</p>
        )}
      </section>

      <section className="mt-8">
        <h2 className="font-mono text-xs uppercase text-muted">{t('artist.bio')}</h2>
        {data.abstract !== null && data.abstract.trim().length > 0 ? (
          <p className="mt-2 max-w-prose font-body leading-relaxed text-strong">{data.abstract}</p>
        ) : (
          <p className="mt-2 font-mono text-xs text-muted">{t('artist.noBio')}</p>
        )}
      </section>

      <section className="mt-8">
        <h2 className="font-mono text-xs uppercase text-muted">{t('artist.lineage')}</h2>
        {data.edges.length > 0 ? (
          <ul className="mt-2 divide-y divide-line border-y border-line">
            {data.edges.map((edge, index) => (
              <EdgeRow key={`${edge.fromId}-${edge.toId}-${index}`} edge={edge} artistId={data.id} />
            ))}
          </ul>
        ) : (
          <p className="mt-2 font-mono text-xs text-muted">{t('artist.noLineage')}</p>
        )}
      </section>

      <section className="mt-8">
        <h2 className="font-display text-2xl text-strong">{t('artist.releases')}</h2>
        {data.releases.length > 0 ? (
          <div className="mt-3 space-y-5">
            {releaseTypeOrder
              .filter((type) => grouped[type].length > 0)
              .map((type) => (
                <ReleaseGroup key={type} type={type} releases={grouped[type]} />
              ))}
          </div>
        ) : (
          <p className="mt-2 font-mono text-xs text-muted">{t('artist.noReleases')}</p>
        )}
      </section>
    </article>
  );
}

function ReleaseGroup({ type, releases }: { type: ReleaseType; releases: Release[] }) {
  const { t } = useTranslation();

  return (
    <div>
      {/* The demo is a first-class type here (SPEC section 4): its own labelled group,
          never hidden under a toggle. */}
      <h3 className="font-mono text-xs uppercase text-accent">{t(`releaseType.${type}`)}</h3>
      <ul className="mt-2 space-y-2">
        {releases.map((release) => (
          <li key={release.id} className="flex items-center gap-3 border-b border-line pb-2">
            <Cover mbid={release.mbid} title={release.title} />
            <span className="min-w-0 flex-1 font-body text-strong">{release.title}</span>
            <span className="shrink-0 font-mono text-xs text-muted">
              {release.releaseDate ? release.releaseDate.slice(0, 4) : '—'}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function EdgeRow({ edge, artistId }: { edge: ArtistEdge; artistId: string }) {
  const { t } = useTranslation();
  const otherId = edge.fromId === artistId ? edge.toId : edge.fromId;
  const years = formatYears(edge.beginDate, edge.endDate);

  return (
    <li className="flex items-baseline justify-between gap-4 py-2">
      <Link
        to="/artist/$artistId"
        params={{ artistId: otherId }}
        className="font-body text-strong no-underline hover:text-accent"
      >
        {t(`edgeKind.${edge.kind}`)}
      </Link>
      <span className="shrink-0 font-mono text-xs text-muted">
        {edge.instruments.length > 0 ? edge.instruments.join(' · ') : null}
        {edge.instruments.length > 0 && years ? ' — ' : null}
        {years}
      </span>
    </li>
  );
}

function formatYears(begin: string | null, end: string | null): string {
  const from = begin ? begin.slice(0, 4) : null;
  const to = end ? end.slice(0, 4) : null;

  if (from === null && to === null) {
    return '';
  }

  return `${from ?? '?'}–${to ?? ''}`;
}

function BackLink() {
  const { t } = useTranslation();
  return (
    <Link to="/" className="font-mono text-xs uppercase text-muted no-underline hover:text-accent">
      ← {t('artist.back')}
    </Link>
  );
}

function groupReleases(releases: Release[]): Record<ReleaseType, Release[]> {
  const groups: Record<ReleaseType, Release[]> = {
    Album: [],
    Ep: [],
    Demo: [],
    Split: [],
    Live: [],
    Compilation: [],
  };

  for (const release of releases) {
    groups[release.type].push(release);
  }

  for (const type of releaseTypeOrder) {
    groups[type].sort((a, b) => (a.releaseDate ?? '9999').localeCompare(b.releaseDate ?? '9999'));
  }

  return groups;
}
