import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useAntiRec, useDarkTwin, useGaps, useReflection, useTrajectory } from '../../core/hooks/useMirror';
import type { ArtistSummary, GapBucket } from '../../core/domain/types';
import { useAuth } from '../auth/AuthProvider';
import { AuthPanel } from '../auth/AuthPanel';
import { RankedName } from '../RankedName';
import { TrajectoryChart } from '../mirror/TrajectoryChart';

// The mirror and your cartography (features C20, C16, B25, B18, B23): the app turning your own rite
// history back on you. Every section reads real data through a core hook and has a designed empty
// state — nothing is invented when there is not yet enough to reflect.
export function MirrorPage() {
  const { t } = useTranslation();
  const { status, isAuthenticated } = useAuth();

  if (status === 'unknown') {
    return <p className="font-mono text-sm text-muted">{t('rite.checking')}</p>;
  }

  if (!isAuthenticated) {
    return <AuthPanel />;
  }

  return (
    <section className="space-y-12">
      <div>
        <h1 className="font-display text-4xl text-strong">{t('mirror.heading')}</h1>
        <p className="mt-2 max-w-prose font-mono text-xs text-muted">{t('mirror.intro')}</p>
      </div>

      <Reflection enabled={isAuthenticated} />
      <TrajectorySection enabled={isAuthenticated} />
      <AntiRecSection enabled={isAuthenticated} />
      <DarkTwinSection enabled={isAuthenticated} />
      <GapsSection enabled={isAuthenticated} />
    </section>
  );
}

// C20 — the mirror.
function Reflection({ enabled }: { enabled: boolean }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useReflection(enabled);

  return (
    <section>
      <h2 className="font-display text-2xl text-strong">{t('mirror.reflectionTitle')}</h2>
      {isLoading ? <p className="mt-2 font-mono text-sm text-muted">{t('mirror.loading')}</p> : null}
      {isError ? <p className="mt-2 font-mono text-sm text-danger">{t('mirror.error')}</p> : null}
      {data !== undefined ? (
        data.hasData ? (
          <p className="mt-3 max-w-prose font-body text-lg text-strong">
            {t('mirror.reflectionStatement', {
              pct: Math.round(data.fraction * 100),
              matching: data.banishedMatching,
              total: data.banishedTotal,
              tag: data.favouriteTag,
            })}
          </p>
        ) : (
          <p className="mt-3 max-w-prose font-body text-sm text-muted">{t('mirror.reflectionEmpty')}</p>
        )
      ) : null}
    </section>
  );
}

// C16 — your trajectory.
function TrajectorySection({ enabled }: { enabled: boolean }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useTrajectory(enabled);

  return (
    <section>
      <h2 className="font-display text-2xl text-strong">{t('mirror.trajectoryTitle')}</h2>
      <p className="mt-1 max-w-prose font-mono text-xs text-muted">{t('mirror.trajectoryHint')}</p>
      {isLoading ? <p className="mt-2 font-mono text-sm text-muted">{t('mirror.loading')}</p> : null}
      {isError ? <p className="mt-2 font-mono text-sm text-danger">{t('mirror.error')}</p> : null}
      {data !== undefined ? (
        data.points.length === 0 ? (
          <p className="mt-3 max-w-prose font-body text-sm text-muted">{t('mirror.trajectoryEmpty')}</p>
        ) : (
          <TrajectoryChart trajectory={data} />
        )
      ) : null}
    </section>
  );
}

// B25 — anti-recommendation.
function AntiRecSection({ enabled }: { enabled: boolean }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useAntiRec(enabled);

  return (
    <section>
      <h2 className="font-display text-2xl text-strong">{t('mirror.antiRecTitle')}</h2>
      <p className="mt-1 max-w-prose font-mono text-xs text-muted">{t('mirror.antiRecHint')}</p>
      {isLoading ? <p className="mt-2 font-mono text-sm text-muted">{t('mirror.loading')}</p> : null}
      {isError ? <p className="mt-2 font-mono text-sm text-danger">{t('mirror.error')}</p> : null}
      {data !== undefined ? (
        data.hasData && data.band !== null ? (
          <div className="mt-3 border border-danger p-5">
            <Link to="/artist/$artistId" params={{ artistId: data.band.id }} className="no-underline">
              <RankedName name={data.band.name} rank={data.band.rank} />
            </Link>
            <p className="mt-2 font-mono text-xs text-muted">
              {data.band.country ?? '—'} · {data.band.formedYear ?? '—'}
            </p>
            <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 font-mono text-xs text-muted">
              <dt className="uppercase">{t('mirror.antiRecToRepulsion')}</dt>
              <dd className="text-strong">{data.band.distanceToRepulsion.toFixed(3)}</dd>
              <dt className="uppercase">{t('mirror.antiRecToTaste')}</dt>
              <dd className="text-strong">
                {Number.isNaN(data.band.distanceToTaste) ? '—' : data.band.distanceToTaste.toFixed(3)}
              </dd>
            </dl>
            {data.band.sharedRejectedTags.length > 0 ? (
              <p className="mt-3 font-mono text-xs text-muted">
                {t('mirror.antiRecShared', { tags: data.band.sharedRejectedTags.join(', ') })}
              </p>
            ) : null}
          </div>
        ) : (
          <p className="mt-3 max-w-prose font-body text-sm text-muted">{t('mirror.antiRecEmpty')}</p>
        )
      ) : null}
    </section>
  );
}

// B18 — the Dark Twin.
function DarkTwinSection({ enabled }: { enabled: boolean }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useDarkTwin(enabled);

  return (
    <section>
      <h2 className="font-display text-2xl text-strong">{t('mirror.darkTwinTitle')}</h2>
      <p className="mt-1 max-w-prose font-mono text-xs text-muted">{t('mirror.darkTwinHint')}</p>
      {isLoading ? <p className="mt-2 font-mono text-sm text-muted">{t('mirror.loading')}</p> : null}
      {isError ? <p className="mt-2 font-mono text-sm text-danger">{t('mirror.error')}</p> : null}
      {data !== undefined ? (
        data.hasData ? (
          <div className="mt-3 border border-line p-5">
            <p className="font-mono text-xs text-muted">
              {t('mirror.darkTwinStats', {
                similarity: Math.round(data.tasteSimilarity * 100),
                disjointness: Math.round(data.disjointness * 100),
                shared: data.sharedCount,
              })}
            </p>
            <h3 className="mt-4 font-mono text-xs uppercase text-accent">{t('mirror.darkTwinTheirs')}</h3>
            <BandList bands={data.theirsOnly} empty={t('mirror.darkTwinTheirsEmpty')} />
          </div>
        ) : (
          <p className="mt-3 max-w-prose font-body text-sm text-muted">{t('mirror.darkTwinEmpty')}</p>
        )
      ) : null}
    </section>
  );
}

// B23 — gaps.
function GapsSection({ enabled }: { enabled: boolean }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useGaps(enabled);

  return (
    <section>
      <h2 className="font-display text-2xl text-strong">{t('mirror.gapsTitle')}</h2>
      <p className="mt-1 max-w-prose font-mono text-xs text-muted">{t('mirror.gapsHint')}</p>
      {isLoading ? <p className="mt-2 font-mono text-sm text-muted">{t('mirror.loading')}</p> : null}
      {isError ? <p className="mt-2 font-mono text-sm text-danger">{t('mirror.error')}</p> : null}
      {data !== undefined ? (
        <div className="mt-4 grid gap-6 sm:grid-cols-3">
          <GapColumn title={t('mirror.gapsDecades')} buckets={data.decades} empty={t('mirror.gapsNone')} />
          <GapColumn title={t('mirror.gapsCountries')} buckets={data.countries} empty={t('mirror.gapsNone')} />
          <GapColumn title={t('mirror.gapsSubgenres')} buckets={data.subgenres} empty={t('mirror.gapsNone')} />
        </div>
      ) : null}
      <p className="mt-4 font-mono text-xs text-muted">
        {t('mirror.gapsAtlasHint')}{' '}
        <Link to="/atlas" className="text-accent no-underline hover:text-strong">
          {t('mirror.gapsToAtlas')}
        </Link>
      </p>
    </section>
  );
}

function GapColumn({ title, buckets, empty }: { title: string; buckets: GapBucket[]; empty: string }) {
  return (
    <div>
      <h3 className="font-mono text-xs uppercase text-muted">{title}</h3>
      {buckets.length === 0 ? (
        <p className="mt-2 font-mono text-xs text-muted">{empty}</p>
      ) : (
        <ul className="mt-2 space-y-1">
          {buckets.map((b) => (
            <li key={b.label} className="flex items-baseline justify-between gap-3 font-mono text-xs">
              <span className="text-strong">{b.label}</span>
              <span className="text-muted">{b.catalogueCount}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function BandList({ bands, empty }: { bands: ArtistSummary[]; empty: string }) {
  if (bands.length === 0) {
    return <p className="mt-2 font-mono text-xs text-muted">{empty}</p>;
  }

  return (
    <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1">
      {bands.map((band) => (
        <li key={band.id}>
          <Link
            to="/artist/$artistId"
            params={{ artistId: band.id }}
            className="font-body text-strong no-underline hover:text-accent"
          >
            {band.name}
          </Link>
        </li>
      ))}
    </ul>
  );
}
