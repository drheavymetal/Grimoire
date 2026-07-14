import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useRelatedSeeds, useSeedCandidates } from '../../core/hooks/useSeedCandidates';
import { useImportLastFm, useSeed } from '../../core/hooks/useColdStart';
import { ApiError } from '../../core/api/client';
import { insertRelatedBelow } from '../../core/domain/seedGrid';
import type { SeedCandidate } from '../../core/domain/types';
import { PageHeader } from '../PageHeader';

const REQUIRED_PICKS = 5;
// The API seeds a taste from at most twenty bands (MaxSeedArtists).
const MAX_PICKS = 20;

// Cold start (D15): a new user has no taste vector, so The Rite cannot run. They seed it by
// choosing bands they already know, or by importing Last.fm (feature C1, currently blocked
// with no API key -> a dignified "not available yet", not a broken error).
//
// The grid GROWS, it never reshuffles: picking a band unfolds its neighbours directly beneath it and
// leaves every row above exactly where it was. Re-ranking the whole grid around the picks would mean
// a band chosen in the seventh row rewrites the six rows above it, and the eye has to start again
// from the top after every click. See core/domain/seedGrid.ts.
export function ColdStart() {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useSeedCandidates(true);
  const related = useRelatedSeeds();
  const seed = useSeed();

  // The grid the user is actually looking at: the fetched one, then whatever their picks unfolded
  // into it. Held here because it is the user's own trail through the catalogue, not server state.
  const [grid, setGrid] = useState<SeedCandidate[]>([]);
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [expanding, setExpanding] = useState<string | null>(null);

  // Seed the grid from the fetched candidates, once. It is never re-seeded from a later fetch: that
  // would throw away the rows the user has already unfolded and read.
  useEffect(() => {
    if (data !== undefined) {
      setGrid((current) => (current.length === 0 ? data : current));
    }
  }, [data]);

  const enough = picked.size >= REQUIRED_PICKS;
  const full = picked.size >= MAX_PICKS;

  async function toggle(band: SeedCandidate, index: number) {
    if (picked.has(band.id)) {
      // Unpicking only unmarks the chip. The bands it unfolded stay: pulling them back out would
      // shift the whole grid under the user's hand, which is the very thing this screen must not do.
      setPicked((current) => {
        const next = new Set(current);
        next.delete(band.id);
        return next;
      });
      return;
    }

    if (full) {
      return;
    }

    setPicked((current) => new Set(current).add(band.id));
    setExpanding(band.id);

    try {
      const neighbours = await related.mutateAsync(band.id);
      setGrid((current) => {
        // The band may have moved if an earlier pick unfolded above it — find it again, do not
        // trust the index the click was made at.
        const at = current.findIndex((row) => row.id === band.id);
        return insertRelatedBelow(current, at === -1 ? index : at, neighbours);
      });
    } catch {
      // A neighbourhood that would not load costs the user nothing: the pick still counts, the grid
      // simply does not grow. No error state for a band they already chose.
    } finally {
      setExpanding(null);
    }
  }

  return (
    <section>
      <PageHeader
        eyebrow={t('coldStart.eyebrow')}
        title={t('coldStart.heading')}
        lead={<p className="font-body text-strong">{t('coldStart.intro')}</p>}
      />
      <p className="mt-3 font-mono text-xs text-muted">
        {t('coldStart.counter', { count: picked.size, required: REQUIRED_PICKS })}
      </p>
      <p className="mt-1 font-mono text-[0.65rem] text-muted">
        {expanding !== null ? t('coldStart.unfolding') : t('coldStart.gridHint')}
      </p>

      {isError ? <p className="mt-4 font-mono text-sm text-danger">{t('coldStart.loadError')}</p> : null}
      {isLoading ? <p className="mt-4 font-mono text-sm text-muted">{t('coldStart.loading')}</p> : null}

      {grid.length > 0 ? (
        <ul className="mt-5 grid grid-cols-2 gap-2 sm:grid-cols-3">
          {grid.map((band, index) => (
            <SeedChip
              key={band.id}
              band={band}
              selected={picked.has(band.id)}
              disabled={full && !picked.has(band.id)}
              onToggle={() => void toggle(band, index)}
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
  disabled = false,
  onToggle,
}: {
  band: SeedCandidate;
  selected: boolean;
  disabled?: boolean;
  onToggle: () => void;
}) {
  return (
    <li>
      <button
        type="button"
        onClick={onToggle}
        disabled={disabled}
        aria-pressed={selected}
        className={`w-full border px-3 py-2 text-left disabled:opacity-40 ${
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
