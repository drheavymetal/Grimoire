import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useAnswerVerdictRound } from '../../core/hooks/useVerdictGame';
import {
  currentRound,
  grade,
  isComplete,
  roundNumber,
  stateToVerdict,
} from '../../core/domain/verdictGame';
import type {
  AnswerRoundResult,
  ArtistDetail,
  VerdictGame,
  VerdictGuess,
} from '../../core/domain/types';
import { RitePlayer } from '../rite/RitePlayer';
import { RevealName } from '../rite/RevealName';

// The verdict game console (GAMES wave). One band at a time, blind, from a friend's grimoire: you
// hear it and call which way THEY judged it. The band is not named, not pictured and not tagged
// until you have answered — the server does not send it (GameView), so there is nothing here to
// leak even by accident.
//
// The audio is the same blind player The Rite uses, pointed at this game's capability URL: the
// origin preview URL never reaches the client (D32). The reveal is the same RevealCard, so a round
// ends in a real discovery and not just a tick.
export function VerdictGameConsole({ game, onExit }: { game: VerdictGame; onExit: () => void }) {
  const { t } = useTranslation();
  const answer = useAnswerVerdictRound(game.id);

  // The last answer's outcome, held locally so the reveal survives the game's refetch. Cleared when
  // the player moves on to the next round.
  const [outcome, setOutcome] = useState<AnswerRoundResult | null>(null);

  const round = currentRound(game.rounds);
  const complete = isComplete(game.rounds);
  const handle = game.opponentHandle !== null ? `@${game.opponentHandle}` : t('games.aFriend');

  function act(verdict: VerdictGuess) {
    if (round === null) {
      return;
    }

    answer.mutate(
      { token: round.token, verdict },
      { onSuccess: (result) => setOutcome(result) },
    );
  }

  // The closing scoreboard: how well you read one person's ear.
  if (complete && outcome === null) {
    return <VerdictGameResult game={game} onExit={onExit} />;
  }

  return (
    <section className="space-y-6">
      <div className="flex items-baseline justify-between gap-4">
        <p className="font-mono text-xs uppercase tracking-[0.2em] text-muted">
          {t('games.roundCounter', { current: roundNumber(game.rounds), total: game.rounds.length })}
        </p>
        <p className="font-mono text-xs uppercase tracking-[0.2em] text-muted">
          {t('games.runningScore', { correct: game.score.correct, answered: game.score.answered })}
        </p>
      </div>

      {outcome === null && round !== null ? (
        <>
          <p className="max-w-prose font-body text-sm text-muted">
            {t('games.prompt', { handle })}
          </p>

          <RitePlayer key={round.token} audioUrl={round.audioUrl} />

          <div className="grid grid-cols-2 gap-2">
            <button
              type="button"
              onClick={() => act('summon')}
              disabled={answer.isPending}
              className="border border-accent px-4 py-3 font-display text-lg text-accent hover:bg-accent hover:text-bg disabled:opacity-40"
            >
              {t('games.guessSummoned')}
            </button>
            <button
              type="button"
              onClick={() => act('banish')}
              disabled={answer.isPending}
              className="border border-line px-4 py-3 font-display text-lg text-muted hover:border-danger hover:text-danger disabled:opacity-40"
            >
              {t('games.guessBanished')}
            </button>
          </div>

          {answer.isError ? (
            <p className="font-mono text-sm text-danger">{t('games.answerError')}</p>
          ) : null}
        </>
      ) : null}

      {outcome !== null ? (
        <RoundOutcome
          outcome={outcome}
          handle={handle}
          onNext={() => setOutcome(null)}
          isLast={outcome.finished}
        />
      ) : null}
    </section>
  );
}

// What one round ended in: whether the read was right, what the friend actually did, and the band at
// last. The verdict is stated plainly — this is the moment the game exposes a banishment, and
// dressing it up would be worse than saying it.
function RoundOutcome({
  outcome,
  handle,
  onNext,
  isLast,
}: {
  outcome: AnswerRoundResult;
  handle: string;
  onNext: () => void;
  isLast: boolean;
}) {
  const { t } = useTranslation();
  const verdict = stateToVerdict(outcome.truth);

  return (
    <div className="space-y-4">
      <div
        className={`border p-6 ${outcome.correct ? 'border-accent' : 'border-line'}`}
      >
        <p
          className={`font-display text-2xl ${outcome.correct ? 'text-accent' : 'text-muted'}`}
        >
          {outcome.correct ? t('games.right') : t('games.wrong')}
        </p>
        <p className="mt-2 max-w-prose font-body text-sm text-muted">
          {verdict === 'summon'
            ? t('games.theySummoned', { handle })
            : t('games.theyBanished', { handle })}
        </p>
      </div>

      {outcome.reveal !== null ? (
        <GameReveal
          artist={outcome.reveal}
          marker={verdict === 'summon' ? t('games.markerSummoned') : t('games.markerBanished')}
        />
      ) : null}

      <button
        type="button"
        onClick={onNext}
        className="w-full border border-accent px-4 py-3 font-mono text-xs uppercase tracking-[0.14em] text-accent hover:bg-accent hover:text-bg"
      >
        {isLast ? t('games.seeResult') : t('games.nextRound')}
      </button>
    </div>
  );
}

// The band, revealed once the round is answered. Deliberately NOT the Rite's RevealCard: that card
// carries the C4 "why you were served this" explanation, and here there is no such why — the band
// was not chosen by your taste, it was chosen because your friend judged it. Rendering the Rite's
// distance-to-your-taste line would be an explanation of something that did not happen.
//
// What it does reuse is RevealName: the name develops through the graded Redaction faces down to the
// cut its RANK earns (D14/D38/D51 — cut 10 clean, cut 70 corroded), exactly as a summon reveals.
function GameReveal({ artist, marker }: { artist: ArtistDetail; marker: string }) {
  const { t } = useTranslation();

  return (
    <div className="flyer border border-line p-6">
      <p className="font-mono text-xs uppercase text-accent">{marker}</p>
      <div className="mt-2">
        <RevealName name={artist.name} rank={artist.rank} />
      </div>

      <dl className="mt-4 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 font-mono text-xs text-muted">
        <dt className="uppercase">{t('artist.origin')}</dt>
        <dd className="text-strong">
          {artist.country ?? '—'}
          {artist.city !== null ? ` · ${artist.city}` : ''}
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

// The end of a game. The grade is about how well you know your friend — never about music trivia,
// which is the distinction the whole game exists to draw.
function VerdictGameResult({ game, onExit }: { game: VerdictGame; onExit: () => void }) {
  const { t } = useTranslation();
  const gradeKey = grade(game.score);
  const handle = game.opponentHandle !== null ? `@${game.opponentHandle}` : t('games.aFriend');

  return (
    <section className="space-y-6">
      <div className="border border-accent p-8 text-center">
        <p className="font-mono text-xs uppercase tracking-[0.3em] text-faint">
          {t('games.resultEyebrow')}
        </p>
        <p className="mt-4 font-display text-6xl text-accent">
          {t('games.finalScore', { correct: game.score.correct, total: game.score.total })}
        </p>
        {gradeKey !== null ? (
          <p className="mx-auto mt-4 max-w-prose font-body text-sm text-muted">
            {t(`games.grade.${gradeKey}`, { handle })}
          </p>
        ) : null}
      </div>

      {/* The turn: they have been told. The reply is theirs to make. */}
      <p className="max-w-prose font-body text-sm text-muted">
        {t('games.turnHandedOver', { handle })}
      </p>

      <button
        type="button"
        onClick={onExit}
        className="w-full border border-line px-4 py-3 font-mono text-xs uppercase tracking-[0.14em] text-muted hover:border-accent hover:text-accent"
      >
        {t('games.backToGames')}
      </button>
    </section>
  );
}
