import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSeedCandidates } from '../../core/hooks/useSeedCandidates';
import { useImportLastFm, useSeed } from '../../core/hooks/useColdStart';
import { ApiError } from '../../core/api/client';
import type { SeedCandidate } from '../../core/domain/types';

const REQUIRED_PICKS = 5;

// Cold start (D15): a new user has no taste vector, so The Rite cannot run. They seed it by
// choosing bands they already know, or by importing Last.fm (feature C1, currently blocked
// with no API key -> a dignified "not available yet", not a broken error).
export function ColdStart() {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useSeedCandidates(true);
  const seed = useSeed();
  const [picked, setPicked] = useState<Set<string>>(new Set());

  function toggle(id: string) {
    setPicked((current) => {
      const next = new Set(current);
      if (next.has(id)) {
        next.delete(id);
      } else if (next.size < 20) {
        next.add(id);
      }
      return next;
    });
  }

  const enough = picked.size >= REQUIRED_PICKS;

  return (
    <section>
      <h1 className="font-display text-4xl text-strong">{t('coldStart.heading')}</h1>
      <p className="mt-2 max-w-prose font-body text-strong">{t('coldStart.intro')}</p>
      <p className="mt-1 font-mono text-xs text-muted">
        {t('coldStart.counter', { count: picked.size, required: REQUIRED_PICKS })}
      </p>

      {isError ? <p className="mt-4 font-mono text-sm text-danger">{t('coldStart.loadError')}</p> : null}
      {isLoading ? <p className="mt-4 font-mono text-sm text-muted">{t('coldStart.loading')}</p> : null}

      {data !== undefined ? (
        <ul className="mt-5 grid grid-cols-2 gap-2 sm:grid-cols-3">
          {data.map((band) => (
            <SeedChip
              key={band.id}
              band={band}
              selected={picked.has(band.id)}
              onToggle={() => toggle(band.id)}
            />
          ))}
        </ul>
      ) : null}

      {seed.isError ? <p className="mt-4 font-mono text-sm text-danger">{t('coldStart.seedError')}</p> : null}

      <button
        type="button"
        disabled={!enough || seed.isPending}
        onClick={() => seed.mutate([...picked])}
        className="mt-6 w-full border border-accent bg-accent px-4 py-3 font-display text-lg text-bg disabled:opacity-40"
      >
        {seed.isPending ? t('coldStart.seeding') : t('coldStart.seed')}
      </button>

      <LastFmImport />
    </section>
  );
}

function SeedChip({
  band,
  selected,
  onToggle,
}: {
  band: SeedCandidate;
  selected: boolean;
  onToggle: () => void;
}) {
  return (
    <li>
      <button
        type="button"
        onClick={onToggle}
        aria-pressed={selected}
        className={`w-full border px-3 py-2 text-left ${
          selected ? 'border-accent bg-accent/10 text-strong' : 'border-line text-muted hover:border-accent'
        }`}
      >
        <span className="block truncate font-body text-strong">{band.name}</span>
        <span className="font-mono text-[0.65rem] text-muted">
          {band.country ?? '—'}
          {band.formedYear !== null ? ` · ${band.formedYear}` : ''}
        </span>
      </button>
    </li>
  );
}

// Last.fm import (feature C1). Blocked without an API key: the endpoint answers 503, which
// we present as a dignified "not available yet" state, never a broken error (blocker Q5).
function LastFmImport() {
  const { t } = useTranslation();
  const importLastFm = useImportLastFm();
  const [username, setUsername] = useState('');

  const unavailable = importLastFm.error instanceof ApiError && importLastFm.error.status === 503;
  const noMatch = importLastFm.error instanceof ApiError && importLastFm.error.status === 404;
  const otherError = importLastFm.isError && !unavailable && !noMatch;

  return (
    <div className="mt-10 border-t border-line pt-6">
      <h2 className="font-mono text-xs uppercase text-muted">{t('coldStart.lastFmHeading')}</h2>
      <p className="mt-2 max-w-prose font-body text-sm text-strong">{t('coldStart.lastFmIntro')}</p>

      <div className="mt-3 flex gap-2">
        <input
          type="text"
          value={username}
          onChange={(event) => setUsername(event.target.value)}
          placeholder={t('coldStart.lastFmPlaceholder')}
          className="min-w-0 flex-1 border border-line bg-panel px-3 py-2 font-body text-strong outline-none focus:border-accent"
          autoComplete="off"
        />
        <button
          type="button"
          disabled={username.trim().length === 0 || importLastFm.isPending}
          onClick={() => importLastFm.mutate(username.trim())}
          className="shrink-0 border border-line px-4 py-2 font-mono text-xs uppercase text-muted hover:border-accent hover:text-accent disabled:opacity-40"
        >
          {importLastFm.isPending ? t('coldStart.lastFmWorking') : t('coldStart.lastFmImport')}
        </button>
      </div>

      {unavailable ? (
        <p className="mt-3 font-mono text-xs text-muted">{t('coldStart.lastFmUnavailable')}</p>
      ) : null}
      {noMatch ? <p className="mt-3 font-mono text-xs text-muted">{t('coldStart.lastFmNoMatch')}</p> : null}
      {otherError ? <p className="mt-3 font-mono text-xs text-danger">{t('coldStart.lastFmError')}</p> : null}
    </div>
  );
}
