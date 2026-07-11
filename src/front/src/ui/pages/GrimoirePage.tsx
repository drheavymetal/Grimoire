import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useGrimoire } from '../../core/hooks/useGrimoire';
import { useGrimoireGraph } from '../../core/hooks/useLineage';
import { useCrossGrimoires, useGrimoireCode } from '../../core/hooks/useCrossedGrimoires';
import { useAuth } from '../auth/AuthProvider';
import { AuthPanel } from '../auth/AuthPanel';
import { GraphCanvas } from '../graph/GraphCanvas';
import { PageHeader } from '../PageHeader';
import { SectionHead } from '../SectionHead';
import type { ArtistSummary, GrimoireEntry } from '../../core/domain/types';

// Your grimoire (feature C17 data): the bands you have summoned, newest first. Rank is null
// across the corpus, so it is not shown here — the display never invents one.
export function GrimoirePage() {
  const { t } = useTranslation();
  const { status, isAuthenticated } = useAuth();
  const grimoire = useGrimoire(isAuthenticated);

  if (status === 'unknown') {
    return <p className="font-mono text-sm text-muted">{t('rite.checking')}</p>;
  }

  if (!isAuthenticated) {
    return <AuthPanel />;
  }

  if (grimoire.isLoading) {
    return <p className="font-mono text-sm text-muted">{t('grimoire.loading')}</p>;
  }

  if (grimoire.isError) {
    return <p className="font-mono text-sm text-danger">{t('grimoire.error')}</p>;
  }

  const entries = grimoire.data ?? [];

  return (
    <section>
      <PageHeader
        eyebrow={t('grimoire.eyebrow')}
        title={t('grimoire.heading')}
        aside={
          <Link to="/rite" className="font-mono text-xs uppercase text-muted no-underline hover:text-accent">
            {t('grimoire.toRite')}
          </Link>
        }
      />

      {entries.length === 0 ? (
        <div className="mt-6 border border-line p-6">
          <p className="font-display text-xl text-strong">{t('grimoire.emptyTitle')}</p>
          <p className="mt-2 max-w-prose font-body text-sm text-muted">{t('grimoire.emptyBody')}</p>
        </div>
      ) : (
        <>
          <ul className="mt-6 divide-y divide-line border-y border-line">
            {entries.map((entry) => (
              <GrimoireRow key={entry.artist.id} entry={entry} />
            ))}
          </ul>
          <GrimoireGraph enabled={isAuthenticated} count={entries.length} />
        </>
      )}

      <CrossedGrimoires enabled={isAuthenticated} />
    </section>
  );
}

// C23 — crossed grimoires: the Dark Twin, but with a friend you named. Share your code, paste
// theirs, and see what they have that you lack (and the reverse, and the common ground).
function CrossedGrimoires({ enabled }: { enabled: boolean }) {
  const { t } = useTranslation();
  const code = useGrimoireCode(enabled);
  const [friend, setFriend] = useState('');
  const [submitted, setSubmitted] = useState('');
  const cross = useCrossGrimoires(submitted);

  const invalid =
    cross.isError && cross.error instanceof Error && 'status' in cross.error &&
    ((cross.error as { status: number }).status === 404 || (cross.error as { status: number }).status === 400);

  return (
    <section className="mt-12">
      <SectionHead title={t('crossed.title')} hint={t('crossed.hint')} />

      <div className="mt-4 border border-line p-4">
        <p className="font-mono text-xs uppercase text-muted">{t('crossed.yourCode')}</p>
        <code className="mt-1 block overflow-x-auto whitespace-nowrap font-mono text-xs text-strong">
          {code.data?.code ?? '—'}
        </code>
      </div>

      <form
        className="mt-4 flex flex-wrap gap-3"
        onSubmit={(event) => {
          event.preventDefault();
          setSubmitted(friend.trim());
        }}
      >
        <input
          type="text"
          value={friend}
          onChange={(event) => setFriend(event.target.value)}
          placeholder={t('crossed.friendPlaceholder')}
          className="min-w-0 flex-1 border border-line bg-panel px-3 py-2 font-mono text-xs text-strong outline-none focus:border-accent"
        />
        <button
          type="submit"
          className="border border-accent px-4 py-2 font-mono text-xs uppercase text-accent hover:bg-accent hover:text-bg"
        >
          {t('crossed.cross')}
        </button>
      </form>

      {submitted.length > 0 && cross.isFetching ? (
        <p className="mt-4 font-mono text-sm text-muted">{t('crossed.crossing')}</p>
      ) : null}
      {submitted.length > 0 && invalid ? (
        <p className="mt-4 font-mono text-sm text-danger">{t('crossed.invalid')}</p>
      ) : null}
      {submitted.length > 0 && cross.isError && !invalid ? (
        <p className="mt-4 font-mono text-sm text-danger">{t('crossed.error')}</p>
      ) : null}

      {submitted.length > 0 && cross.data !== undefined && !cross.isFetching ? (
        <div className="mt-4 space-y-6">
          <CrossColumn title={t('crossed.theirsOnly')} empty={t('crossed.theirsEmpty')} bands={cross.data.theirsOnly} accent />
          <CrossColumn title={t('crossed.shared')} empty={t('crossed.sharedEmpty')} bands={cross.data.shared} />
          <CrossColumn title={t('crossed.yoursOnly')} empty={t('crossed.yoursEmpty')} bands={cross.data.yoursOnly} />
        </div>
      ) : null}
    </section>
  );
}

function CrossColumn({
  title,
  empty,
  bands,
  accent = false,
}: {
  title: string;
  empty: string;
  bands: ArtistSummary[];
  accent?: boolean;
}) {
  return (
    <div>
      <h3 className={`font-mono text-xs uppercase ${accent ? 'text-accent' : 'text-muted'}`}>{title}</h3>
      {bands.length === 0 ? (
        <p className="mt-1 font-mono text-xs text-muted">{empty}</p>
      ) : (
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
      )}
    </div>
  );
}

// C17 — your grimoire as a graph: the summoned bands and the edges between them. With one band
// there is nothing to connect, so the graph is only offered once there are at least two.
function GrimoireGraph({ enabled, count }: { enabled: boolean; count: number }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useGrimoireGraph(enabled && count >= 2);

  if (count < 2) {
    return null;
  }

  return (
    <section className="mt-10">
      <SectionHead title={t('lineage.grimoireGraphTitle')} hint={t('lineage.grimoireGraphHint')} />
      {isLoading ? (
        <p className="mt-3 font-mono text-sm text-muted">{t('lineage.loading')}</p>
      ) : isError ? (
        <p className="mt-3 font-mono text-sm text-danger">{t('lineage.error')}</p>
      ) : data !== undefined ? (
        <GraphCanvas graph={data} />
      ) : null}
    </section>
  );
}

function GrimoireRow({ entry }: { entry: GrimoireEntry }) {
  const { t } = useTranslation();
  const { artist, resolvedAt } = entry;

  return (
    <li>
      <Link
        to="/artist/$artistId"
        params={{ artistId: artist.id }}
        className="flex items-baseline justify-between gap-4 py-3 no-underline"
      >
        <span className="font-display text-xl text-strong">{artist.name}</span>
        <span className="shrink-0 font-mono text-xs text-muted">
          {artist.country ?? t('search.countryUnknown')} · {t('grimoire.summonedOn', { date: resolvedAt.slice(0, 10) })}
        </span>
      </Link>
    </li>
  );
}
