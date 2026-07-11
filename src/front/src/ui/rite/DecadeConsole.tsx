import { useMemo, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useServeDecade, useGuessDecade } from '../../core/hooks/useDecade';
import { addRound, decadeOptions, EMPTY_SCOREBOARD, type Scoreboard } from '../../core/domain/decade';
import { comfortToPercentileBand } from '../../core/domain/rite';
import type { DecadeScoreResult, DecadeServed, GuessOutcome } from '../../core/domain/types';
import { RitePlayer } from './RitePlayer';
import { RevealName } from './RevealName';

type Phase = 'idle' | 'listening' | 'scored' | 'empty';

// Guess the decade (feature C27): The Rite with a scoreboard. A band plays 45 s blind; the player
// bets a decade, a country and a subgenre; then the band is revealed and each bet is scored against
// its real data (server-side, honest). It trains the ear, which is literally the app's mission. The
// scoreboard accumulates across the session in a pure reducer — no persistence, no migration.
export function DecadeConsole() {
  const { t } = useTranslation();
  const serve = useServeDecade();
  const guess = useGuessDecade();

  const decades = useMemo(() => decadeOptions(new Date().getFullYear()), []);

  const [comfort, setComfort] = useState(0.5);
  const [phase, setPhase] = useState<Phase>('idle');
  const [served, setServed] = useState<DecadeServed | null>(null);
  const [result, setResult] = useState<DecadeScoreResult | null>(null);
  const [board, setBoard] = useState<Scoreboard>(EMPTY_SCOREBOARD);

  const [decade, setDecade] = useState<number>(decades[Math.min(1, decades.length - 1)] ?? decades[0]);
  const [country, setCountry] = useState('');
  const [subgenre, setSubgenre] = useState('');

  const band = comfortToPercentileBand(comfort);

  function play() {
    setResult(null);
    serve.mutate(comfort, {
      onSuccess: (r) => {
        if (r === null) {
          setServed(null);
          setPhase('empty');
        } else {
          setServed(r);
          setPhase('listening');
        }
      },
    });
  }

  function submit() {
    if (served === null) {
      return;
    }

    guess.mutate(
      {
        token: served.token,
        guess: {
          decade,
          country: country.trim() === '' ? null : country.trim().toUpperCase(),
          subgenre: subgenre.trim() === '' ? null : subgenre.trim(),
        },
      },
      {
        onSuccess: (r) => {
          setResult(r);
          setBoard((prev) => addRound(prev, r));
          setPhase('scored');
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
        <h1 className="font-display text-4xl text-strong">{t('decade.heading')}</h1>
        <Link
          to="/rite"
          className="font-mono text-xs uppercase text-muted no-underline hover:text-accent"
        >
          {t('decade.toRite')}
        </Link>
      </div>
      <p className="mt-2 max-w-prose font-mono text-xs text-muted">{t('decade.subheading')}</p>

      {/* Running scoreboard (session): honest N of M, built from the pure reducer. */}
      {board.rounds > 0 ? (
        <p className="mt-4 font-mono text-sm text-accent">
          {t('decade.scoreboard', { points: board.points, max: board.maxPoints, rounds: board.rounds })}
        </p>
      ) : null}

      <div className="mt-4 border border-line bg-panel p-5">
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
      </div>

      {phase !== 'listening' ? (
        <button
          type="button"
          onClick={play}
          disabled={serve.isPending}
          className="mt-5 w-full border border-accent bg-accent px-4 py-3 font-display text-lg text-bg disabled:opacity-40"
        >
          {serve.isPending ? t('decade.summoning') : phase === 'idle' ? t('decade.play') : t('decade.next')}
        </button>
      ) : null}

      {serve.isError ? <p className="mt-4 font-mono text-sm text-danger">{t('decade.serveError')}</p> : null}

      {/* No scorable band in reach (HTTP 204): a designed empty state (D25). */}
      {phase === 'empty' ? (
        <div className="mt-6 border border-line p-6">
          <p className="font-display text-xl text-strong">{t('decade.emptyTitle')}</p>
          <p className="mt-2 max-w-prose font-body text-sm text-muted">{t('decade.emptyBody')}</p>
        </div>
      ) : null}

      {served !== null && phase === 'listening' ? (
        <div className="mt-6 space-y-4">
          <RitePlayer key={served.token} audioUrl={served.audioUrl} />

          {/* The bet: decade, country and subgenre. Country and subgenre are optional — bet only
              what you are sure of. Nothing about the band is shown before the bet (anti-leak). */}
          <div className="border border-line bg-panel p-5">
            <p className="font-mono text-xs uppercase text-muted">{t('decade.yourBet')}</p>
            <div className="mt-3 grid gap-3 sm:grid-cols-3">
              <label className="block">
                <span className="font-mono text-[0.65rem] uppercase text-muted">{t('decade.decade')}</span>
                <select
                  value={decade}
                  onChange={(event) => setDecade(Number(event.target.value))}
                  aria-label={t('decade.decade')}
                  className="mt-1 w-full border border-line bg-bg px-2 py-1 font-mono text-sm text-strong outline-none focus:border-accent"
                >
                  {decades.map((d) => (
                    <option key={d} value={d}>
                      {d}s
                    </option>
                  ))}
                </select>
              </label>
              <label className="block">
                <span className="font-mono text-[0.65rem] uppercase text-muted">{t('decade.country')}</span>
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
                <span className="font-mono text-[0.65rem] uppercase text-muted">{t('decade.subgenre')}</span>
                <input
                  type="text"
                  value={subgenre}
                  onChange={(event) => setSubgenre(event.target.value)}
                  placeholder={t('decade.subgenrePlaceholder')}
                  className="mt-1 w-full border border-line bg-bg px-2 py-1 font-mono text-sm text-strong outline-none focus:border-accent"
                />
              </label>
            </div>
            <button
              type="button"
              onClick={submit}
              disabled={guess.isPending}
              className="mt-4 w-full border border-accent px-4 py-3 font-display text-lg text-accent hover:bg-accent hover:text-bg disabled:opacity-40"
            >
              {guess.isPending ? t('decade.scoring') : t('decade.revealAndScore')}
            </button>
            {guess.isError ? <p className="mt-3 font-mono text-sm text-danger">{t('decade.guessError')}</p> : null}
          </div>
        </div>
      ) : null}

      {phase === 'scored' && result !== null ? <ScoreCard result={result} /> : null}
    </section>
  );
}

// The reveal and score of a round: the name develops in (RevealName), then a line per dimension —
// what you bet, the truth, and whether you hit — and the round total.
function ScoreCard({ result }: { result: DecadeScoreResult }) {
  const { t } = useTranslation();

  return (
    <div className="flyer mt-6 border border-accent p-6">
      <p className="font-mono text-xs uppercase text-accent">
        {t('decade.roundScore', { points: result.totalPoints, max: result.maxPoints })}
      </p>
      <div className="mt-2">
        <RevealName name={result.artist.name} rank={result.artist.rank} />
      </div>

      <dl className="mt-5 space-y-3">
        <ScoreRow label={t('decade.decade')} dim={result.decade} />
        <ScoreRow label={t('decade.country')} dim={result.country} />
        <ScoreRow label={t('decade.subgenre')} dim={result.subgenre} />
      </dl>

      <Link
        to="/artist/$artistId"
        params={{ artistId: result.artist.id }}
        className="mt-5 inline-block font-mono text-xs uppercase text-accent no-underline hover:text-strong"
      >
        {t('rite.openFiche')} →
      </Link>
    </div>
  );
}

function ScoreRow({ label, dim }: { label: string; dim: DecadeScoreResult['decade'] }) {
  const { t } = useTranslation();

  return (
    <div className="grid grid-cols-[auto_1fr_auto] items-baseline gap-x-4">
      <span className="font-mono text-xs uppercase text-muted">{label}</span>
      <span className="font-mono text-xs text-strong">
        {dim.guess === '' ? t('decade.noBet') : dim.guess} <span className="text-muted">→ {dim.actual}</span>
      </span>
      <span className={`font-mono text-xs uppercase ${outcomeClass(dim.outcome)}`}>
        {t(`decade.outcome.${dim.outcome}`)} · {dim.points}
      </span>
    </div>
  );
}

function outcomeClass(outcome: GuessOutcome): string {
  if (outcome === 'hit') {
    return 'text-accent';
  }

  if (outcome === 'close') {
    return 'text-strong';
  }

  return 'text-muted';
}
