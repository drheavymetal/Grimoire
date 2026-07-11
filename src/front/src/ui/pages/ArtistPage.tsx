import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useArtist } from '../../core/hooks/useArtist';
import { useArtistCredits, usePivotalRelease } from '../../core/hooks/useArtistCredits';
import { releaseTypeOrder } from '../../core/domain/rank';
import { splitPerformers, hasCredits } from '../../core/domain/credits';
import { ApiError } from '../../core/api/client';
import type {
  ArtistDetail,
  PerformerCredit,
  PivotalRelease,
  Release,
  ReleaseCredits,
  ReleaseType,
  TurnoverMember,
} from '../../core/domain/types';
import { Cover } from '../Cover';
import { RankedName } from '../RankedName';
import { GiftButton } from '../GiftButton';
import { LineupTimeline } from '../lineup/LineupTimeline';
import { Bloodline } from '../lineage/Bloodline';
import { Diaspora } from '../lineage/Diaspora';
import { MemberBands } from '../lineage/MemberBands';
import { RabbitHole } from '../lineage/RabbitHole';

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

  return <ArtistBody data={data} />;
}

function ArtistBody({ data }: { data: ArtistDetail }) {
  const { t } = useTranslation();
  // B9 — per-release credits, and B12 — the pivotal release. Both read real data through core/
  // hooks; a band with no credits or no lineup change degrades to a designed empty state (R2).
  const { data: credits } = useArtistCredits(data.id);
  const { data: pivotal } = usePivotalRelease(data.id);

  const creditsByRelease = new Map<string, ReleaseCredits>(
    (credits ?? []).map((c) => [c.releaseId, c]),
  );

  const grouped = groupReleases(data.releases);

  return (
    <article>
      <BackLink />
      {/* Q1 wired (D14/D38): the name renders in the Redaction cut its rank earns — Known crisp,
          Nameless corroded, an unknown rank crisp (unknown is not rare, D35). The typography is
          the datum. Corrosion is only ever the band name, never the app mark (D27). */}
      <h1 className="mt-3">
        <RankedName name={data.name} rank={data.rank} className="text-5xl text-strong" />
      </h1>

      {/* The Gantt is the hero, in the header-photo slot (DESIGN 6): Grimoire has no band
          photos, so it shows the band's structure in time. B7/B8; reused for B10 (a person's
          rows are their bands). */}
      <LineupTimeline edges={data.edges} releases={data.releases} viewedKind={data.kind} />

      <dl className="mt-6 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 font-mono text-xs text-muted">
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

      {/* B12 — "the disc where everything changed": the release with the most lineup turnover
          around it. Shown only when the band's lineup actually churned around a dated release;
          otherwise the endpoint returns nothing and this section is absent (no invented drama). */}
      {pivotal ? <PivotalReleaseCallout pivotal={pivotal} /> : null}

      <section className="mt-8">
        <h2 className="font-display text-2xl text-strong">{t('artist.releases')}</h2>
        {data.releases.length > 0 ? (
          <div className="mt-3 space-y-5">
            {releaseTypeOrder
              .filter((type) => grouped[type].length > 0)
              .map((type) => (
                <ReleaseGroup
                  key={type}
                  type={type}
                  releases={grouped[type]}
                  creditsByRelease={creditsByRelease}
                  pivotalReleaseId={pivotal?.releaseId ?? null}
                />
              ))}
          </div>
        ) : (
          <p className="mt-2 font-mono text-xs text-muted">{t('artist.noReleases')}</p>
        )}
      </section>

      {/* Movement IV — Lineage. Bloodline is the ego graph of any artist (B16). Bands also get
          their diaspora (B11) and a rabbit hole (C8); people get the bands they played in (B3). */}
      <Bloodline artistId={data.id} />

      {data.kind === 'Person' ? <MemberBands personId={data.id} enabled={true} /> : null}

      {data.kind === 'Group' ? (
        <>
          <Diaspora artistId={data.id} />
          <RabbitHole artistId={data.id} />
          {/* C22 — send this band as a blind, signed gift (signed-in only). */}
          <GiftButton artistId={data.id} />
        </>
      ) : null}
    </article>
  );
}

function ReleaseGroup({
  type,
  releases,
  creditsByRelease,
  pivotalReleaseId,
}: {
  type: ReleaseType;
  releases: Release[];
  creditsByRelease: Map<string, ReleaseCredits>;
  pivotalReleaseId: string | null;
}) {
  const { t } = useTranslation();

  return (
    <div>
      {/* The demo is a first-class type here (SPEC section 4): its own labelled group,
          never hidden under a toggle. */}
      <h3 className="font-mono text-xs uppercase text-accent">{t(`releaseType.${type}`)}</h3>
      <ul className="mt-2 space-y-2">
        {releases.map((release) => (
          <ReleaseRow
            key={release.id}
            release={release}
            credits={creditsByRelease.get(release.id)}
            isPivotal={release.id === pivotalReleaseId}
          />
        ))}
      </ul>
    </div>
  );
}

// One release row (B5) with its expandable per-release credits (B9). The credits are fetched once
// for the whole discography; this row shows whichever the map holds for it, and a designed
// "no credits" state otherwise (R2 — the underground is thin, the ficha must degrade with dignity).
function ReleaseRow({
  release,
  credits,
  isPivotal,
}: {
  release: Release;
  credits: ReleaseCredits | undefined;
  isPivotal: boolean;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);

  return (
    <li className="border-b border-line pb-2">
      <div className="flex items-center gap-3">
        <Cover mbid={release.mbid} title={release.title} />
        <button
          type="button"
          onClick={() => setOpen((v) => !v)}
          aria-expanded={open}
          className="min-w-0 flex-1 text-left font-body text-strong hover:text-accent"
        >
          {release.title}
          {isPivotal ? (
            <span className="ml-2 font-mono text-[0.6rem] uppercase text-accent">
              {t('pivotal.badge')}
            </span>
          ) : null}
        </button>
        <span className="shrink-0 font-mono text-xs text-muted">
          {release.releaseDate ? release.releaseDate.slice(0, 4) : '—'}
        </span>
      </div>

      {open ? (
        <div className="mt-2 pl-[calc(3rem+0.75rem)]">
          {credits !== undefined && hasCredits(credits) ? (
            <ReleaseCreditsPanel credits={credits} />
          ) : (
            <p className="font-mono text-xs text-muted">{t('artist.noCredits')}</p>
          )}
        </div>
      ) : null}
    </li>
  );
}

// The per-release credits panel (B9): official members and guests kept apart (the D9 distinction),
// each with their instruments, plus production. Names click through to the person's page.
function ReleaseCreditsPanel({ credits }: { credits: ReleaseCredits }) {
  const { t } = useTranslation();
  const { members, guests } = splitPerformers(credits.performers);

  return (
    <div className="space-y-3">
      {members.length > 0 ? (
        <CreditList title={t('artist.creditsMembers')} performers={members} />
      ) : null}
      {guests.length > 0 ? (
        <CreditList title={t('artist.creditsGuests')} performers={guests} />
      ) : null}
      {credits.production.length > 0 ? (
        <div>
          <h4 className="font-mono text-[0.6rem] uppercase text-muted">{t('artist.creditsProduction')}</h4>
          <ul className="mt-1 space-y-0.5">
            {credits.production.map((p) => (
              <li key={`${p.artistId}-${p.role}`} className="font-body text-sm text-strong">
                <Link
                  to="/artist/$artistId"
                  params={{ artistId: p.artistId }}
                  className="no-underline hover:text-accent"
                >
                  {p.name}
                </Link>
                <span className="ml-2 font-mono text-xs text-muted">{t(`creditRole.${p.role}`)}</span>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </div>
  );
}

function CreditList({
  title,
  performers,
}: {
  title: string;
  performers: PerformerCredit[];
}) {
  return (
    <div>
      <h4 className="font-mono text-[0.6rem] uppercase text-muted">{title}</h4>
      <ul className="mt-1 space-y-0.5">
        {performers.map((p) => (
          <li key={p.artistId} className="font-body text-sm text-strong">
            <Link
              to="/artist/$artistId"
              params={{ artistId: p.artistId }}
              className="no-underline hover:text-accent"
            >
              {p.name}
            </Link>
            {p.instruments.length > 0 ? (
              <span className="ml-2 font-mono text-xs text-muted">{p.instruments.join(', ')}</span>
            ) : null}
          </li>
        ))}
      </ul>
    </div>
  );
}

// B12 — the callout for "the disc where everything changed": the release with the most lineup
// turnover around its date, and who came in and went out near it.
function PivotalReleaseCallout({ pivotal }: { pivotal: PivotalRelease }) {
  const { t } = useTranslation();

  return (
    <section className="mt-8 border border-accent/40 p-4">
      <h2 className="font-display text-xl text-strong">{t('pivotal.title')}</h2>
      <p className="mt-1 font-mono text-xs text-muted">{t('pivotal.hint')}</p>

      <p className="mt-3 font-body text-strong">
        <span className="font-display text-lg text-accent">{pivotal.title}</span>
        {pivotal.year !== null ? (
          <span className="ml-2 font-mono text-xs text-muted">{pivotal.year}</span>
        ) : null}
      </p>

      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        {pivotal.joined.length > 0 ? (
          <TurnoverList title={t('pivotal.joined')} members={pivotal.joined} />
        ) : null}
        {pivotal.left.length > 0 ? (
          <TurnoverList title={t('pivotal.left')} members={pivotal.left} />
        ) : null}
      </div>
    </section>
  );
}

function TurnoverList({
  title,
  members,
}: {
  title: string;
  members: TurnoverMember[];
}) {
  return (
    <div>
      <h3 className="font-mono text-[0.6rem] uppercase text-muted">{title}</h3>
      <ul className="mt-1 space-y-0.5">
        {members.map((m) => (
          <li key={m.id} className="font-body text-sm text-strong">
            <Link
              to="/artist/$artistId"
              params={{ artistId: m.id }}
              className="no-underline hover:text-accent"
            >
              {m.name}
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
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
