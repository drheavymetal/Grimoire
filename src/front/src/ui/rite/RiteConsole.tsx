import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useServe, useResolve } from '../../core/hooks/useRite';
import { comfortToPercentileBand } from '../../core/domain/rite';
import type { RiteAction, RiteReveal, RiteScope, ServedRite } from '../../core/domain/types';
import { RitePlayer } from './RitePlayer';
import { RevealCard } from './RevealCard';

type Phase = 'idle' | 'listening' | 'revealed' | 'blindResolved' | 'empty';

// The optional genre lanes (feature added 2026-07-15). Keys mirror the backend RiteGenres catalogue;
// an unknown key just falls back to a fully open rite server-side, so drift degrades safely. Labels
// are the genres' universal English names (metal subgenres are not translated).
const RITE_GENRES: ReadonlyArray<{ key: string; label: string }> = [
  // audit-ok: static genre catalogue mirroring the backend RiteGenres; labels are universal
  // English genre names, deliberately not translated (see comment above), not mock content.
  { key: 'black-metal', label: 'Black Metal' },
  { key: 'death-metal', label: 'Death Metal' },
  { key: 'doom-metal', label: 'Doom Metal' },
  { key: 'thrash-metal', label: 'Thrash Metal' },
  { key: 'heavy-metal', label: 'Heavy Metal' },
  { key: 'power-metal', label: 'Power Metal' },
  { key: 'speed-metal', label: 'Speed Metal' },
  { key: 'sludge', label: 'Sludge' },
  { key: 'grindcore', label: 'Grindcore' },
  { key: 'viking-metal', label: 'Viking Metal' },
  { key: 'folk-metal', label: 'Folk Metal' },
  { key: 'symphonic-metal', label: 'Symphonic Metal' },
  { key: 'gothic-metal', label: 'Gothic Metal' },
  { key: 'progressive', label: 'Progressive' },
  { key: 'stoner', label: 'Stoner' },
  { key: 'metalcore', label: 'Metalcore' },
  { key: 'folk', label: 'Folk' },
  { key: 'punk', label: 'Punk' },
  { key: 'hardcore', label: 'Hardcore' },
  { key: 'rock', label: 'Rock' },
];

// The Rite console (features B13, B14, C4, C13). The slider sets the ring percentiles, the
// player serves a band blind, and Summon/Banish/Again resolve it. Only a summon reveals the
// band; banish and again stay blind on purpose (C3/C20).
// A scope arriving via search params (from a ficha chip's "Invocar a ciegas") narrows the pool but
// keeps the tasting blind — the card never shows the band's name, genre or theme (the app's thesis).
export function RiteConsole({ scope }: { scope?: RiteScope }) {
  const { t } = useTranslation();
  const serve = useServe();
  const resolve = useResolve();

  const isScoped =
    scope !== undefined &&
    (scope.genreNeedle !== undefined || scope.themeNeedle !== undefined);

  const [comfort, setComfort] = useState(0.5);
  const [genre, setGenre] = useState('');
  const [country, setCountry] = useState('');
  const [decadeFrom, setDecadeFrom] = useState('');
  const [decadeTo, setDecadeTo] = useState('');

  const [phase, setPhase] = useState<Phase>('idle');
  const [served, setServed] = useState<ServedRite | null>(null);
  const [reveal, setReveal] = useState<RiteReveal | null>(null);
  const [lastAction, setLastAction] = useState<RiteAction | null>(null);

  const band = comfortToPercentileBand(comfort);

  function invoke() {
    setReveal(null);
    setLastAction(null);
    serve.mutate(
      {
        comfort,
        genre: genre === '' ? null : genre,
        country: country.trim() === '' ? null : country.trim().toUpperCase(),
        decadeFrom: decadeFrom.trim() === '' ? null : Number(decadeFrom),
        decadeTo: decadeTo.trim() === '' ? null : Number(decadeTo),
        // The incoming scope narrows the pool; it never touches what the card reveals (stays blind).
        genreNeedle: scope?.genreNeedle,
        themeNeedle: scope?.themeNeedle,
        themeKind: scope?.themeKind,
      },
      {
        onSuccess: (result) => {
          if (result === null) {
            setServed(null);
            setPhase('empty');
          } else {
            setServed(result);
            setPhase('listening');
          }
        },
      },
    );
  }

  function act(action: RiteAction) {
    if (served === null) {
      return;
    }

    resolve.mutate(
      { token: served.token, action },
      {
        onSuccess: (result) => {
          setLastAction(action);
          if (action === 'summon' && result.reveal !== null) {
            setReveal(result.reveal);
            setPhase('revealed');
          } else {
            setReveal(null);
            setPhase('blindResolved');
          }
        },
      },
    );
  }

  const percentLabel = t('rite.percentileWindow', {
    low: Math.round(band.low * 100),
    high: Math.round(band.high * 100),
  });

  return (
    <section>
      <div className="flex items-baseline justify-between">
        <h1 className="font-display text-4xl text-strong">{t('rite.heading')}</h1>
        <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
          <Link
            to="/duel"
            className="font-mono text-xs uppercase text-muted no-underline hover:text-accent"
          >
            {t('rite.toDuel')}
          </Link>
          <Link
            to="/decade"
            className="font-mono text-xs uppercase text-muted no-underline hover:text-accent"
          >
            {t('rite.toDecade')}
          </Link>
          <Link
            to="/grimoire"
            className="font-mono text-xs uppercase text-muted no-underline hover:text-accent"
          >
            {t('rite.toGrimoire')}
          </Link>
        </div>
      </div>
      <p className="mt-2 max-w-prose font-mono text-xs text-muted">{t('rite.subheading')}</p>

      {/* A scoped rite says only that the pool is narrowed — never the band's identity (stays blind). */}
      {isScoped ? (
        <p className="mt-3 inline-block border border-accent/40 px-3 py-1 font-mono text-xs uppercase text-accent">
          {t('rite.scoped')}
        </p>
      ) : null}

      {/* Slider Comfort <-> Abyss (B14): the honest percentile window is shown, not a decoration. */}
      <div className="mt-6 border border-line bg-panel p-5">
        <div className="flex items-center justify-between font-mono text-xs uppercase text-muted">
          <span>{t('rite.comfort')}</span>
          <span>{t('rite.abyss')}</span>
        </div>
        <input
          type="range"
          min={0}
          max={1}
          step={0.05}
          value={comfort}
          onChange={(event) => setComfort(Number(event.target.value))}
          aria-label={t('rite.sliderLabel')}
          className="mt-2 w-full accent-[var(--accent)]"
        />
        <p className="mt-2 font-mono text-xs text-muted">{percentLabel}</p>

        {/* Optional genre lane: narrows the pool but keeps the tasting blind. Default is fully open. */}
        <label className="mt-4 block">
          <span className="font-mono text-[0.65rem] uppercase text-muted">{t('rite.genre')}</span>
          <select
            value={genre}
            onChange={(event) => setGenre(event.target.value)}
            className="mt-1 w-full border border-line bg-bg px-2 py-2 font-mono text-sm text-strong outline-none focus:border-accent"
          >
            <option value="">{t('rite.genreAny')}</option>
            {RITE_GENRES.map((g) => (
              <option key={g.key} value={g.key}>
                {g.label}
              </option>
            ))}
          </select>
        </label>

        <details className="mt-4">
          <summary className="cursor-pointer font-mono text-xs uppercase text-muted hover:text-accent">
            {t('rite.filters')}
          </summary>
          <div className="mt-3 grid grid-cols-3 gap-2">
            <label className="block">
              <span className="font-mono text-[0.65rem] uppercase text-muted">{t('rite.country')}</span>
              <input
                type="text"
                value={country}
                onChange={(event) => setCountry(event.target.value)}
                placeholder={t('rite.countryPlaceholder')}
                maxLength={2}
                className="mt-1 w-full border border-line bg-bg px-2 py-1 font-mono text-sm uppercase text-strong outline-none focus:border-accent"
              />
            </label>
            <label className="block">
              <span className="font-mono text-[0.65rem] uppercase text-muted">{t('rite.decadeFrom')}</span>
              <input
                type="number"
                value={decadeFrom}
                onChange={(event) => setDecadeFrom(event.target.value)}
                placeholder="1990"
                className="mt-1 w-full border border-line bg-bg px-2 py-1 font-mono text-sm text-strong outline-none focus:border-accent"
              />
            </label>
            <label className="block">
              <span className="font-mono text-[0.65rem] uppercase text-muted">{t('rite.decadeTo')}</span>
              <input
                type="number"
                value={decadeTo}
                onChange={(event) => setDecadeTo(event.target.value)}
                placeholder="2010"
                className="mt-1 w-full border border-line bg-bg px-2 py-1 font-mono text-sm text-strong outline-none focus:border-accent"
              />
            </label>
          </div>
        </details>
      </div>

      <button
        type="button"
        onClick={invoke}
        disabled={serve.isPending}
        className="mt-5 w-full border border-accent bg-accent px-4 py-3 font-display text-lg text-bg disabled:opacity-40"
      >
        {serve.isPending ? t('rite.summoning') : t('rite.invoke')}
      </button>

      {serve.isError ? (
        <p className="mt-4 font-mono text-sm text-danger">{t('rite.serveError')}</p>
      ) : null}

      {/* Empty ring (HTTP 204): a designed empty state that says WHY, not a hole (D25). */}
      {phase === 'empty' ? (
        <div className="mt-6 border border-line p-6">
          <p className="font-display text-xl text-strong">{t('rite.emptyTitle')}</p>
          <p className="mt-2 max-w-prose font-body text-sm text-muted">{t('rite.emptyBody')}</p>
        </div>
      ) : null}

      {served !== null && (phase === 'listening' || phase === 'revealed' || phase === 'blindResolved') ? (
        <div className="mt-6 space-y-4">
          <RitePlayer key={served.token} audioUrl={served.audioUrl} />

          {phase === 'listening' ? (
            <div className="grid grid-cols-3 gap-2">
              <ActionButton onClick={() => act('summon')} disabled={resolve.isPending} variant="summon">
                {t('rite.summon')}
              </ActionButton>
              <ActionButton onClick={() => act('again')} disabled={resolve.isPending} variant="again">
                {t('rite.again')}
              </ActionButton>
              <ActionButton onClick={() => act('banish')} disabled={resolve.isPending} variant="banish">
                {t('rite.banish')}
              </ActionButton>
            </div>
          ) : null}

          {resolve.isError ? (
            <p className="font-mono text-sm text-danger">{t('rite.resolveError')}</p>
          ) : null}

          {phase === 'revealed' && reveal !== null ? (
            <RevealCard reveal={reveal} marker={t('rite.summoned')} />
          ) : null}

          {phase === 'blindResolved' && lastAction !== null ? (
            <div className="border border-line p-6">
              <p className="font-display text-xl text-strong">
                {lastAction === 'banish' ? t('rite.banishedTitle') : t('rite.againTitle')}
              </p>
              <p className="mt-2 max-w-prose font-body text-sm text-muted">
                {lastAction === 'banish' ? t('rite.banishedBody') : t('rite.againBody')}
              </p>
              <button
                type="button"
                onClick={invoke}
                className="mt-4 border border-accent px-4 py-2 font-mono text-xs uppercase text-accent hover:bg-accent hover:text-bg"
              >
                {t('rite.next')}
              </button>
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

function ActionButton({
  onClick,
  disabled,
  variant,
  children,
}: {
  onClick: () => void;
  disabled: boolean;
  variant: 'summon' | 'banish' | 'again';
  children: React.ReactNode;
}) {
  const classes =
    variant === 'summon'
      ? 'border-accent text-accent hover:bg-accent hover:text-bg'
      : variant === 'banish'
        ? 'border-danger text-danger hover:bg-danger hover:text-bg'
        : 'border-line text-muted hover:border-strong hover:text-strong';

  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={`border px-3 py-3 font-display text-lg disabled:opacity-40 ${classes}`}
    >
      {children}
    </button>
  );
}

