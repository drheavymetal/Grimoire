import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useDuel, useResolveDuel } from '../../core/hooks/useDuel';
import { comfortToPercentileBand } from '../../core/domain/rite';
import type { DuelServed, RiteReveal } from '../../core/domain/types';
import { RitePlayer } from './RitePlayer';
import { RevealCard } from './RevealCard';

type Phase = 'idle' | 'dueling' | 'revealed' | 'empty';

// The blind duel (feature C2, DECISIONS D16): two bands served blind, side by side. The user hears
// both and picks one. The pairwise preference (Bradley-Terry) teaches the taste vector more than a
// lone like — the server moves it toward the winner and away from the loser. Both bands stay blind
// until the choice; only the winner is then revealed, and it enters the grimoire.
export function DuelConsole() {
  const { t } = useTranslation();
  const duel = useDuel();
  const resolve = useResolveDuel();

  const [comfort, setComfort] = useState(0.5);
  const [phase, setPhase] = useState<Phase>('idle');
  const [pair, setPair] = useState<DuelServed | null>(null);
  const [reveal, setReveal] = useState<RiteReveal | null>(null);

  const band = comfortToPercentileBand(comfort);

  function begin() {
    setReveal(null);
    duel.mutate(
      { comfort },
      {
        onSuccess: (result) => {
          if (result === null) {
            setPair(null);
            setPhase('empty');
          } else {
            setPair(result);
            setPhase('dueling');
          }
        },
      },
    );
  }

  function choose(side: 'left' | 'right') {
    if (pair === null) {
      return;
    }

    const winner = side === 'left' ? pair.left : pair.right;
    const loser = side === 'left' ? pair.right : pair.left;

    resolve.mutate(
      { winnerToken: winner.token, loserToken: loser.token },
      {
        onSuccess: (result) => {
          setReveal(result.reveal);
          setPhase('revealed');
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
      <p className="font-mono text-[0.7rem] uppercase tracking-[0.28em] text-accent">{t('duel.eyebrow')}</p>
      <div className="mt-1 flex items-baseline justify-between">
        <h1 className="font-display text-4xl text-strong">{t('duel.heading')}</h1>
        <Link
          to="/rite"
          className="font-mono text-xs uppercase text-muted no-underline hover:text-accent"
        >
          {t('duel.toRite')}
        </Link>
      </div>
      <p className="mt-2 max-w-prose font-mono text-xs text-muted">{t('duel.subheading')}</p>

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
      </div>

      <button
        type="button"
        onClick={begin}
        disabled={duel.isPending}
        className="mt-5 w-full border border-accent bg-accent px-4 py-3 font-display text-lg text-bg disabled:opacity-40"
      >
        {duel.isPending ? t('duel.summoning') : phase === 'idle' ? t('duel.begin') : t('duel.again')}
      </button>

      {duel.isError ? <p className="mt-4 font-mono text-sm text-danger">{t('duel.error')}</p> : null}

      {/* The ring could not offer two distinct bands (HTTP 204): a designed empty state (D25). */}
      {phase === 'empty' ? (
        <div className="mt-6 border border-line p-6">
          <p className="font-display text-xl text-strong">{t('duel.emptyTitle')}</p>
          <p className="mt-2 max-w-prose font-body text-sm text-muted">{t('duel.emptyBody')}</p>
        </div>
      ) : null}

      {pair !== null && (phase === 'dueling' || phase === 'revealed') ? (
        <div className="mt-6 grid gap-4 sm:grid-cols-2">
          <DuelSideCard
            label={t('duel.sideA')}
            audioUrl={pair.left.audioUrl}
            chooseLabel={t('duel.choose')}
            onChoose={() => choose('left')}
            disabled={phase !== 'dueling' || resolve.isPending}
          />
          <DuelSideCard
            label={t('duel.sideB')}
            audioUrl={pair.right.audioUrl}
            chooseLabel={t('duel.choose')}
            onChoose={() => choose('right')}
            disabled={phase !== 'dueling' || resolve.isPending}
          />
        </div>
      ) : null}

      {resolve.isError ? <p className="mt-4 font-mono text-sm text-danger">{t('duel.resolveError')}</p> : null}

      {phase === 'revealed' && reveal !== null ? (
        <div className="mt-6">
          <RevealCard reveal={reveal} marker={t('duel.winner')} />
        </div>
      ) : null}
    </section>
  );
}

// One blind side of the duel: its own player (each with a distinct key so the two audio elements do
// not share state) and a "choose this one" button. No name, cover, country or genre — blind to the end.
function DuelSideCard({
  label,
  audioUrl,
  chooseLabel,
  onChoose,
  disabled,
}: {
  label: string;
  audioUrl: string;
  chooseLabel: string;
  onChoose: () => void;
  disabled: boolean;
}) {
  return (
    <div className="space-y-3">
      <p className="font-display text-2xl text-strong">{label}</p>
      <RitePlayer key={audioUrl} audioUrl={audioUrl} />
      <button
        type="button"
        onClick={onChoose}
        disabled={disabled}
        className="w-full border border-accent px-3 py-3 font-display text-lg text-accent hover:bg-accent hover:text-bg disabled:opacity-40"
      >
        {chooseLabel}
      </button>
    </div>
  );
}
