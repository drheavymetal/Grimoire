import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useGrimoire } from '../../core/hooks/useGrimoire';
import { useAuth } from '../auth/AuthProvider';
import { AuthPanel } from '../auth/AuthPanel';
import type { GrimoireEntry } from '../../core/domain/types';

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
      <div className="flex items-baseline justify-between">
        <h1 className="font-display text-4xl text-strong">{t('grimoire.heading')}</h1>
        <Link to="/rite" className="font-mono text-xs uppercase text-muted no-underline hover:text-accent">
          {t('grimoire.toRite')}
        </Link>
      </div>

      {entries.length === 0 ? (
        <div className="mt-6 border border-line p-6">
          <p className="font-display text-xl text-strong">{t('grimoire.emptyTitle')}</p>
          <p className="mt-2 max-w-prose font-body text-sm text-muted">{t('grimoire.emptyBody')}</p>
        </div>
      ) : (
        <ul className="mt-6 divide-y divide-line border-y border-line">
          {entries.map((entry) => (
            <GrimoireRow key={entry.artist.id} entry={entry} />
          ))}
        </ul>
      )}
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
