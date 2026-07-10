import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useArtistSearch } from '../../core/hooks/useArtistSearch';
import { useDebouncedValue } from '../../core/hooks/useDebouncedValue';
import type { ArtistSummary } from '../../core/domain/types';

export function SearchPage() {
  const { t } = useTranslation();
  const [term, setTerm] = useState('');
  const debounced = useDebouncedValue(term, 300);
  const { data, isFetching, isError } = useArtistSearch(debounced);

  const showResults = debounced.trim().length >= 2;
  const results = data ?? [];

  return (
    <section>
      <h1 className="font-display text-4xl text-strong">{t('search.heading')}</h1>
      <p className="mt-2 font-mono text-xs text-muted">{t('search.hint')}</p>

      <input
        type="search"
        value={term}
        onChange={(event) => setTerm(event.target.value)}
        placeholder={t('search.placeholder')}
        className="mt-5 w-full border border-line bg-panel px-4 py-3 font-body text-strong outline-none focus:border-accent"
        autoComplete="off"
      />

      <div className="mt-6">
        {isError ? (
          <p className="font-mono text-sm text-danger">{t('search.error')}</p>
        ) : null}

        {!isError && isFetching ? (
          <p className="font-mono text-sm text-muted">{t('search.searching')}</p>
        ) : null}

        {!isError && !isFetching && showResults && results.length === 0 ? (
          <p className="font-mono text-sm text-muted">{t('search.empty')}</p>
        ) : null}

        {!isError && results.length > 0 ? (
          <div>
            <p className="mb-3 font-mono text-xs uppercase text-muted">
              {t('search.resultCount', { count: results.length })}
            </p>
            <ul className="divide-y divide-line border-y border-line">
              {results.map((artist) => (
                <ResultRow key={artist.id} artist={artist} />
              ))}
            </ul>
          </div>
        ) : null}
      </div>
    </section>
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
