import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useAnswerGuessRound } from '../../core/hooks/useGuessGame';
import {
  currentGuessRound,
  guessGrade,
  guessRoundNumber,
  isGuessComplete,
  isTypedRound,
} from '../../core/domain/guessGame';
import type { AnswerGuessRoundResult, ArtistDetail, GuessGame, GuessRound } from '../../core/domain/types';
import { RitePlayer } from '../rite/RitePlayer';
import { RevealName } from '../rite/RevealName';

// The guess-the-band console (D67). One band at a time, blind, from the player's OWN grimoire: they
// summoned it once, with no name and no cover, and the question is whether they know who it was.
//
// The band is not named, not pictured and not tagged until the round is answered — the server does not
// send it (GameView), so there is nothing here to leak even by accident. In the four-name mode the
// answer IS on screen, shuffled among three decoys the server drew from this same grimoire and
// ordered by a hash of the round's id: nothing in this file knows which one is true, and it could not
// give it away if it tried.
//
// The audio is the same blind player The Rite uses, pointed at this game's capability URL: the origin
// preview URL never reaches the client (D32).
export function GuessGameConsole({ game, onExit }: { game: GuessGame; onExit: () => void }) {
  const { t } = useTranslation();
  const answer = useAnswerGuessRound(game.id);

  // The last answer's outcome, held locally so the reveal survives the game's refetch. Cleared when
  // the player moves on to the next round.
  const [outcome, setOutcome] = useState<AnswerGuessRoundResult | null>(null);
  const [typed, setTyped] = useState('');

  const round = currentGuessRound(game.rounds);
  const complete = isGuessComplete(game.rounds);

  function pick(artistId: string) {
    if (round === null) {
      return;
    }

    answer.mutate({ token: round.token, artistId }, { onSuccess: (result) => setOutcome(result) });
  }

  function submitTyped() {
    if (round === null || typed.trim().length === 0) {
      return;
    }

    answer.mutate({ token: round.token, name: typed }, { onSuccess: (result) => setOutcome(result) });
  }

  function next() {
    setOutcome(null);
    setTyped('');
  }

  if (complete && outcome === null) {
    return <GuessGameResult game={game} onExit={onExit} />;
  }

  return (
    <section className="space-y-6">
      <div className="flex items-baseline justify-between gap-4">
        <p className="font-mono text-xs uppercase tracking-[0.2em] text-muted">
          {t('guess.roundCounter', { current: guessRoundNumber(game.rounds), total: game.rounds.length })}
        </p>
        <p className="font-mono text-xs uppercase tracking-[0.2em] text-muted">
          {t('guess.runningScore', { points: game.score.points })}
        </p>
      </div>

      {outcome === null && round !== null ? (
        <>
          <p className="max-w-prose font-body text-sm text-muted">{t('guess.prompt')}</p>

          <RitePlayer key={round.token} audioUrl={round.audioUrl} />

          {isTypedRound(round) ? (
            <TypedAnswer
              value={typed}
              onChange={setTyped}
              onSubmit={submitTyped}
              pending={answer.isPending}
            />
          ) : (
            <ChoiceButtons round={round} onPick={pick} pending={answer.isPending} />
          )}

          {answer.isError ? (
            <p className="font-mono text-sm text-danger">{t('guess.answerError')}</p>
          ) : null}
        </>
      ) : null}

      {outcome !== null ? (
        <GuessOutcome outcome={outcome} onNext={next} isLast={outcome.finished} />
      ) : null}
    </section>
  );
}

// The four names. Rendered exactly as they arrive: the server already shuffled them by a hash of the
// round's id, and re-sorting them here — alphabetically, say, to look tidy — would be the one change
// that could put the answer somewhere predictable. They are identical in shape by design; there is
// nothing to style differently, and nothing that could be.
function ChoiceButtons({
  round,
  onPick,
  pending,
}: {
  round: GuessRound;
  onPick: (artistId: string) => void;
  pending: boolean;
}) {
  return (
    <div className="grid gap-2 sm:grid-cols-2">
      {(round.choices ?? []).map((choice) => (
        <button
          key={choice.artistId}
          type="button"
          onClick={() => onPick(choice.artistId)}
          disabled={pending}
          className="border border-line px-4 py-3 font-display text-lg text-strong hover:border-accent hover:text-accent disabled:opacity-40"
        >
          {choice.name}
        </button>
      ))}
    </div>
  );
}

// The typed mode. A plain field, because that is the whole mechanic: no list, no autocomplete, no
// search — an autocomplete against the catalogue would hand back the very answer the round is asking
// for, one letter at a time.
function TypedAnswer({
  value,
  onChange,
  onSubmit,
  pending,
}: {
  value: string;
  onChange: (value: string) => void;
  onSubmit: () => void;
  pending: boolean;
}) {
  const { t } = useTranslation();

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit();
      }}
      className="space-y-3"
    >
      <input
        type="text"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        disabled={pending}
        autoComplete="off"
        spellCheck={false}
        placeholder={t('guess.typePlaceholder')}
        aria-label={t('guess.typeLabel')}
        className="w-full border border-line bg-transparent px-4 py-3 font-display text-lg text-strong placeholder:text-faint focus:border-accent focus:outline-none disabled:opacity-40"
      />

      {/* Said out loud, because a player who fears the tilde will not risk the answer they know. */}
      <p className="font-mono text-xs text-faint">{t('guess.typeForgiving')}</p>

      <button
        type="submit"
        disabled={pending || value.trim().length === 0}
        className="w-full border border-accent px-4 py-3 font-mono text-xs uppercase tracking-[0.14em] text-accent hover:bg-accent hover:text-bg disabled:opacity-40"
      >
        {t('guess.submitName')}
      </button>
    </form>
  );
}

// What one round ended in. The band is named at last — which is the point of the whole game, and for
// a band you loved blind and could not name, it is closer to an introduction than to a score.
function GuessOutcome({
  outcome,
  onNext,
  isLast,
}: {
  outcome: AnswerGuessRoundResult;
  onNext: () => void;
  isLast: boolean;
}) {
  const { t } = useTranslation();

  return (
    <div className="space-y-4">
      <div className={`border p-6 ${outcome.correct ? 'border-accent' : 'border-line'}`}>
        <p className={`font-display text-2xl ${outcome.correct ? 'text-accent' : 'text-muted'}`}>
          {outcome.correct ? t('guess.right') : t('guess.wrong')}
        </p>
        <p className="mt-2 max-w-prose font-body text-sm text-muted">
          {outcome.correct ? t('guess.rightBody') : t('guess.wrongBody')}
        </p>
      </div>

      {outcome.reveal !== null ? <GuessReveal artist={outcome.reveal} /> : null}

      <button
        type="button"
        onClick={onNext}
        className="w-full border border-accent px-4 py-3 font-mono text-xs uppercase tracking-[0.14em] text-accent hover:bg-accent hover:text-bg"
      >
        {isLast ? t('guess.seeResult') : t('guess.nextRound')}
      </button>
    </div>
  );
}

// The band, revealed once the round is answered. Deliberately NOT the Rite's RevealCard: that card
// carries the C4 "why you were served this" explanation, and there is no such why here — the band was
// not chosen by your taste, it was chosen BECAUSE of your taste, which is a different sentence.
//
// It reuses RevealName: the name develops through the graded Redaction faces down to the cut its RANK
// earns (D14/D38/D51), exactly as a summon reveals. That is the right flourish for this game in
// particular — the rarer the band, the more corroded the name you failed to remember.
function GuessReveal({ artist }: { artist: ArtistDetail }) {
  const { t } = useTranslation();

  return (
    <div className="flyer border border-line p-6">
      <p className="font-mono text-xs uppercase text-accent">{t('guess.markerWasYours')}</p>
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

// The end of a game. The grade is about the joke the game is built on — you already chose these with
// your ears — so even the bottom band is not an insult: not knowing the name is the app's argument,
// not the player's failure.
function GuessGameResult({ game, onExit }: { game: GuessGame; onExit: () => void }) {
  const { t } = useTranslation();
  const gradeKey = guessGrade(game.score);
  const handle = game.opponentHandle !== null ? `@${game.opponentHandle}` : t('guess.aFriend');

  return (
    <section className="space-y-6">
      <div className="border border-accent p-8 text-center">
        <p className="font-mono text-xs uppercase tracking-[0.3em] text-faint">
          {t('guess.resultEyebrow')}
        </p>
        <p className="mt-4 font-display text-6xl text-accent">
          {t('guess.finalScore', { correct: game.score.correct, total: game.score.total })}
        </p>
        <p className="mt-2 font-mono text-xs uppercase tracking-[0.2em] text-muted">
          {t('guess.finalPoints', { points: game.score.points })}
        </p>
        {gradeKey !== null ? (
          <p className="mx-auto mt-4 max-w-prose font-body text-sm text-muted">
            {t(`guess.grade.${gradeKey}`)}
          </p>
        ) : null}
      </div>

      {/* The turn, when there is one. A solo game tells nobody, and says so rather than going quiet. */}
      <p className="max-w-prose font-body text-sm text-muted">
        {game.opponentId !== null
          ? t('guess.turnHandedOver', { handle })
          : t('guess.soloClosing')}
      </p>

      <button
        type="button"
        onClick={onExit}
        className="w-full border border-line px-4 py-3 font-mono text-xs uppercase tracking-[0.14em] text-muted hover:border-accent hover:text-accent"
      >
        {t('guess.backToGames')}
      </button>
    </section>
  );
}
