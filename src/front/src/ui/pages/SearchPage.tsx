import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useArtistSearch } from '../../core/hooks/useArtistSearch';
import { useSemanticSearch } from '../../core/hooks/useSemanticSearch';
import { useDebouncedValue } from '../../core/hooks/useDebouncedValue';
import type { ArtistSummary, SemanticHit } from '../../core/domain/types';

type Mode = 'name' | 'meaning';

export function SearchPage() {
  const { t } = useTranslation();
  const [term, setTerm] = useState('');
  const [mode, setMode] = useState<Mode>('name');
  const debounced = useDebouncedValue(term, 300);

  const trigram = useArtistSearch(mode === 'name' ? debounced : '');
  // B2 — semantic search over the embedding space: "something like Neurosis but slower".
  const semantic = useSemanticSearch(debounced, mode === 'meaning');

  const active = mode === 'name' ? trigram : semantic;
  const showResults = debounced.trim().length >= (mode === 'name' ? 2 : 3);
  const results = active.data ?? [];

  return (
    <section>
      {/* The splash is a surface of impact (Q2 hybrid, DESIGN §2): the photocopied flyer grain in
          light mode, clean in dark (the cassette). The .flyer class paints grain only in light. */}
      <div className="flyer -mx-5 -mt-8 border-b border-line px-5 pb-6 pt-8">
        <h1 className="font-display text-4xl text-strong">{t('search.heading')}</h1>
        <p className="mt-2 font-mono text-xs text-muted">
          {mode === 'name' ? t('search.hint') : t('search.semanticHint')}
        </p>
      </div>

      <div className="mt-5 flex gap-2">
        <ModeTab active={mode === 'name'} onClick={() => setMode('name')} label={t('search.byName')} />
        <ModeTab active={mode === 'meaning'} onClick={() => setMode('meaning')} label={t('search.byMeaning')} />
      </div>

      <input
        type="search"
        value={term}
        onChange={(event) => setTerm(event.target.value)}
        placeholder={mode === 'name' ? t('search.placeholder') : t('search.semanticPlaceholder')}
        className="mt-3 w-full border border-line bg-panel px-4 py-3 font-body text-strong outline-none focus:border-accent"
        autoComplete="off"
      />

      <div className="mt-6">
        {active.isError ? (
          <p className="font-mono text-sm text-danger">
            {mode === 'meaning' ? t('search.semanticError') : t('search.error')}
          </p>
        ) : null}

        {!active.isError && active.isFetching ? (
          <p className="font-mono text-sm text-muted">
            {mode === 'meaning' ? t('search.thinking') : t('search.searching')}
          </p>
        ) : null}

        {!active.isError && !active.isFetching && showResults && results.length === 0 ? (
          <p className="font-mono text-sm text-muted">{t('search.empty')}</p>
        ) : null}

        {!active.isError && results.length > 0 ? (
          <div>
            <p className="mb-3 font-mono text-xs uppercase text-muted">
              {t('search.resultCount', { count: results.length })}
            </p>
            <ul className="divide-y divide-line border-y border-line">
              {mode === 'name'
                ? (results as ArtistSummary[]).map((artist) => <ResultRow key={artist.id} artist={artist} />)
                : (results as SemanticHit[]).map((hit) => <SemanticRow key={hit.id} hit={hit} />)}
            </ul>
          </div>
        ) : null}
      </div>
    </section>
  );
}

function ModeTab({ active, onClick, label }: { active: boolean; onClick: () => void; label: string }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`border px-3 py-1.5 font-mono text-xs uppercase ${
        active ? 'border-accent text-accent' : 'border-line text-muted hover:text-accent'
      }`}
    >
      {label}
    </button>
  );
}

function ResultRow({ artist }: { artist: ArtistSummary }) {
  const { t } = useTranslation();
  const formed = artist.formedYear !== null
    ? t('search.formedIn', { year: artist.formedYear })
    : t('search.formedUnknown');
  const origin = artist.country ?? t('search.countryUnknown');

  return (
    <li>
      <Link
        to="/artist/$artistId"
        params={{ artistId: artist.id }}
        className="flex items-baseline justify-between gap-4 py-3 no-underline"
      >
        <span className="font-display text-xl text-strong">{artist.name}</span>
        <span className="shrink-0 font-mono text-xs text-muted">
          {origin} · {formed}
        </span>
      </Link>
    </li>
  );
}

function SemanticRow({ hit }: { hit: SemanticHit }) {
  const { t } = useTranslation();
  const origin = hit.country ?? t('search.countryUnknown');

  return (
    <li>
      <Link
        to="/artist/$artistId"
        params={{ artistId: hit.id }}
        className="flex items-baseline justify-between gap-4 py-3 no-underline"
      >
        <span className="font-display text-xl text-strong">{hit.name}</span>
        <span className="shrink-0 font-mono text-xs text-muted">
          {origin} · {t('search.distance', { distance: hit.distance.toFixed(3) })}
        </span>
      </Link>
    </li>
  );
}
