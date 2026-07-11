import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useServe, useResolve } from '../../core/hooks/useRite';
import { comfortToPercentileBand } from '../../core/domain/rite';
import type { RiteAction, RiteReveal, ServedRite } from '../../core/domain/types';
import { RitePlayer } from './RitePlayer';
import { RevealName } from './RevealName';

type Phase = 'idle' | 'listening' | 'revealed' | 'blindResolved' | 'empty';

// The Rite console (features B13, B14, C4, C13). The slider sets the ring percentiles, the
// player serves a band blind, and Summon/Banish/Again resolve it. Only a summon reveals the
// band; banish and again stay blind on purpose (C3/C20).
export function RiteConsole() {
  const { t } = useTranslation();
  const serve = useServe();
  const resolve = useResolve();

  const [comfort, setComfort] = useState(0.5);
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
        country: country.trim() === '' ? null : country.trim().toUpperCase(),
        decadeFrom: decadeFrom.trim() === '' ? null : Number(decadeFrom),
        decadeTo: decadeTo.trim() === '' ? null : Number(decadeTo),
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
        <Link
          to="/grimoire"
          className="font-mono text-xs uppercase text-muted no-underline hover:text-accent"
        >
          {t('rite.toGrimoire')}
        </Link>
      </div>
      <p className="mt-2 max-w-prose font-mono text-xs text-muted">{t('rite.subheading')}</p>

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

          {phase === 'revealed' && reveal !== null ? <Reveal reveal={reveal} /> : null}

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

// The reveal (only after a summon): the band name develops in, then its origin, tags, the
// C4 explanation (distance, shared tags, shared members), and a link to the full ficha.
function Reveal({ reveal }: { reveal: RiteReveal }) {
  const { t } = useTranslation();
  const { artist, why } = reveal;

  return (
    // The reveal is a surface of impact (Q2 hybrid, DESIGN §2): the photocopied flyer grain in
    // light mode. Dark mode stays clean (the cassette). The .flyer class paints grain only in light.
    <div className="flyer border border-accent p-6">
      <p className="font-mono text-xs uppercase text-accent">{t('rite.summoned')}</p>
      <div className="mt-2">
        <RevealName name={artist.name} rank={artist.rank} />
      </div>

      <dl className="mt-4 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 font-mono text-xs text-muted">
        <dt className="uppercase">{t('artist.origin')}</dt>
        <dd className="text-strong">
          {artist.country ?? '—'}
          {artist.city ? ` · ${artist.city}` : ''}
        </dd>
        <dt className="uppercase">{t('artist.formed')}</dt>
        <dd className="text-strong">{artist.formedYear ?? '—'}</dd>
      </dl>

      {artist.tags.length > 0 ? (
        <ul className="mt-3 flex flex-wrap gap-2">
          {artist.tags.map((tag) => (
            <li key={tag} className="border border-line px-2 py-1 font-mono text-xs text-strong">
              {tag}
            </li>
          ))}
        </ul>
      ) : null}

      {/* C4 explainability: without "why you were served this", a strange recommender looks broken. */}
      <div className="mt-5 border-t border-line pt-4">
        <h3 className="font-mono text-xs uppercase text-muted">{t('rite.why')}</h3>
        <p className="mt-2 font-mono text-xs text-muted">
          {t('rite.whyDistance', { distance: why.distance.toFixed(3) })}
        </p>
        {why.sharedTags.length > 0 ? (
          <p className="mt-1 font-mono text-xs text-muted">
            {t('rite.whyTags', { tags: why.sharedTags.join(', ') })}
          </p>
        ) : null}
        {why.sharedMembers.length > 0 ? (
          <p className="mt-1 font-mono text-xs text-muted">
            {t('rite.whyMembers', { members: why.sharedMembers.join(', ') })}
          </p>
        ) : null}
        {why.sharedTags.length === 0 && why.sharedMembers.length === 0 ? (
          <p className="mt-1 font-mono text-xs text-muted">{t('rite.whyNothingShared')}</p>
        ) : null}
      </div>

      <Link
        to="/artist/$artistId"
        params={{ artistId: artist.id }}
        className="mt-5 inline-block font-mono text-xs uppercase text-accent no-underline hover:text-strong"
      >
        {t('rite.openFiche')} →
      </Link>
    </div>
  );
}
