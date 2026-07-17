import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useFriends } from '../../core/hooks/useFriends';
import {
  useSetVerdictGameConsent,
  useStartVerdictGame,
  useVerdictGame,
  useVerdictGameAvailability,
  useVerdictGameConsent,
  useVerdictGames,
} from '../../core/hooks/useVerdictGame';
import {
  useGuessGame,
  useGuessGameAvailability,
  useGuessGames,
  useStartGuessGame,
} from '../../core/hooks/useGuessGame';
import type {
  Friend,
  GuessDifficulty,
  GuessGameSummary,
  VerdictGameSummary,
} from '../../core/domain/types';
import { useAuth } from '../auth/AuthProvider';
import { AuthPanel } from '../auth/AuthPanel';
import { PageHeader } from '../PageHeader';
import { VerdictGameConsole } from '../games/VerdictGameConsole';
import { GuessGameConsole } from '../games/GuessGameConsole';

// The GAMES page. Two games, and they are opposites on purpose.
//
// "Did you summon it, or banish it?" (D66) tests how well you know ONE PERSON'S ear, over their
// grimoire. "Guess the band" (D67) tests whether you know YOUR OWN — the bands you already chose
// blind, with no name attached, and may never have learned the names of.
//
// Neither is a music quiz, and that is not a coincidence: naming bands in general rewards the canon
// you arrived with, which is the axis this whole app exists to invert (D43). Bounded to a grimoire —
// somebody's, or your own — the question stops being "how much do you know" and starts being one only
// you can answer.
//
// Both are turn-based through the inbox, nothing realtime (D60): you play, they get a notification
// with your score, they play back when they feel like it.
type GameKind = 'verdict' | 'guess';

type OpenGame = { kind: GameKind; id: string };

export function GamesPage() {
  const { t } = useTranslation();
  const { status, isAuthenticated } = useAuth();

  if (status === 'unknown') {
    return <p className="font-mono text-sm text-muted">{t('rite.checking')}</p>;
  }

  if (!isAuthenticated) {
    return <AuthPanel />;
  }

  return <GamesBody />;
}

function GamesBody() {
  const { t } = useTranslation();
  const [open, setOpen] = useState<OpenGame | null>(null);

  const friends = useFriends(true);
  const verdictGames = useVerdictGames(true);
  const guessGames = useGuessGames(true);

  const verdictGame = useVerdictGame(open?.kind === 'verdict' ? open.id : null);
  const guessGame = useGuessGame(open?.kind === 'guess' ? open.id : null);

  const start = useStartVerdictGame();

  const accepted = (friends.data ?? []).filter((f) => f.status === 'Accepted');

  // A game is open: the console owns the screen.
  if (open !== null) {
    const active = open.kind === 'verdict' ? verdictGame : guessGame;

    if (active.isLoading) {
      return <p className="font-mono text-sm text-muted">{t('games.loading')}</p>;
    }

    if (active.isError || active.data === undefined) {
      return (
        <div className="space-y-4">
          <p className="font-mono text-sm text-danger">{t('games.loadError')}</p>
          <button
            type="button"
            onClick={() => setOpen(null)}
            className="border border-line px-4 py-2 font-mono text-xs uppercase text-muted hover:border-accent hover:text-accent"
          >
            {t('games.backToGames')}
          </button>
        </div>
      );
    }

    return (
      <section className="space-y-8">
        <PageHeader
          eyebrow={open.kind === 'verdict' ? t('games.eyebrow') : t('guess.eyebrow')}
          title={open.kind === 'verdict' ? t('games.heading') : t('guess.heading')}
        />
        {open.kind === 'verdict' && verdictGame.data !== undefined ? (
          <VerdictGameConsole game={verdictGame.data} onExit={() => setOpen(null)} />
        ) : null}
        {open.kind === 'guess' && guessGame.data !== undefined ? (
          <GuessGameConsole game={guessGame.data} onExit={() => setOpen(null)} />
        ) : null}
      </section>
    );
  }

  return (
    <section className="space-y-12">
      <PageHeader
        eyebrow={t('games.eyebrow')}
        title={t('games.heading')}
        lead={t('games.lead')}
      />

      <GuessGamePanel
        friends={accepted}
        onOpen={(id) => setOpen({ kind: 'guess', id })}
      />

      <div className="space-y-8 border-t border-line pt-12">
        <div>
          <h2 className="font-display text-2xl text-strong">{t('games.verdictHeading')}</h2>
          <p className="mt-2 max-w-prose font-body text-sm text-muted">{t('games.verdictLead')}</p>
        </div>

        <ConsentPanel />

        <div className="space-y-4">
          <h2 className="font-mono text-xs uppercase tracking-[0.2em] text-muted">
            {t('games.pickFriend')}
          </h2>

          {friends.isLoading ? (
            <p className="font-mono text-sm text-muted">{t('games.loading')}</p>
          ) : friends.isError ? (
            <p className="font-mono text-sm text-danger">{t('games.friendsError')}</p>
          ) : accepted.length === 0 ? (
            // No friends yet: an honest empty state that says what to do, not an error.
            <div className="border border-dashed border-line p-8 text-center">
              <p className="font-display text-xl text-strong">{t('games.noFriendsTitle')}</p>
              <p className="mx-auto mt-2 max-w-prose font-body text-sm text-muted">
                {t('games.noFriendsBody')}
              </p>
            </div>
          ) : (
            <ul className="divide-y divide-line border-y border-line">
              {accepted.map((friend) => (
                <FriendRow
                  key={friend.userId}
                  friend={friend}
                  onPlay={() =>
                    start.mutate(friend.userId, {
                      onSuccess: (created) => setOpen({ kind: 'verdict', id: created.id }),
                    })
                  }
                  starting={start.isPending}
                />
              ))}
            </ul>
          )}

          {start.isError ? (
            <p className="font-mono text-sm text-danger">{t('games.startError')}</p>
          ) : null}
        </div>
      </div>

      <GuessHistory games={guessGames.data ?? []} onOpen={(id) => setOpen({ kind: 'guess', id })} />
      <GameHistory games={verdictGames.data ?? []} onOpen={(id) => setOpen({ kind: 'verdict', id })} />
    </section>
  );
}

// Guess the band (D67): pick a difficulty, then play it alone or send the score to a friend.
//
// The whole panel hangs off ONE honest question — can your own grimoire make a game? — asked per
// difficulty, because the answer differs: three summons can be typed but cannot fill a four-name
// choice. When it cannot, the reason is a sentence about real data, and the cure is always the same
// and always stated: go play The Rite.
function GuessGamePanel({ friends, onOpen }: { friends: Friend[]; onOpen: (id: string) => void }) {
  const { t } = useTranslation();
  const [difficulty, setDifficulty] = useState<GuessDifficulty>('normal');
  const [opponentId, setOpponentId] = useState<string | null>(null);

  const availability = useGuessGameAvailability(difficulty, true);
  const start = useStartGuessGame();

  const playable = availability.data?.playable === true;
  const reason = availability.data?.reason ?? null;

  return (
    <div className="space-y-6">
      <div>
        <h2 className="font-display text-2xl text-strong">{t('guess.heading')}</h2>
        <p className="mt-2 max-w-prose font-body text-sm text-muted">{t('guess.lead')}</p>
      </div>

      <div className="space-y-4 border border-line p-6">
        <div className="space-y-2">
          <h3 className="font-mono text-xs uppercase tracking-[0.2em] text-muted">
            {t('guess.difficultyTitle')}
          </h3>
          <div className="grid gap-2 sm:grid-cols-2">
            <DifficultyButton
              active={difficulty === 'normal'}
              onClick={() => setDifficulty('normal')}
              title={t('guess.normalTitle')}
              body={t('guess.normalBody')}
            />
            <DifficultyButton
              active={difficulty === 'hard'}
              onClick={() => setDifficulty('hard')}
              title={t('guess.hardTitle')}
              body={t('guess.hardBody')}
            />
          </div>
        </div>

        <div className="space-y-2">
          <h3 className="font-mono text-xs uppercase tracking-[0.2em] text-muted">
            {t('guess.opponentTitle')}
          </h3>

          {/* Solo is first and is the default: this game works with nobody, and a friend is a bonus,
              not a requirement. The friend list is a plain select — with four friends, a picker. */}
          <div className="flex flex-wrap items-center gap-3">
            <select
              value={opponentId ?? ''}
              onChange={(event) => setOpponentId(event.target.value === '' ? null : event.target.value)}
              aria-label={t('guess.opponentLabel')}
              className="border border-line bg-transparent px-3 py-2 font-mono text-xs text-strong focus:border-accent focus:outline-none"
            >
              <option value="">{t('guess.solo')}</option>
              {friends.map((friend) => (
                <option key={friend.userId} value={friend.userId}>
                  {friend.handle !== null ? `@${friend.handle}` : t('guess.aFriend')}
                </option>
              ))}
            </select>

            <p className="font-mono text-xs text-faint">
              {opponentId === null ? t('guess.soloExplain') : t('guess.challengeExplain')}
            </p>
          </div>
        </div>

        {availability.isLoading ? (
          <p className="font-mono text-xs text-faint">{t('games.checking')}</p>
        ) : playable ? (
          <div className="flex flex-wrap items-center gap-3">
            <button
              type="button"
              onClick={() =>
                start.mutate(
                  { difficulty, opponentId },
                  { onSuccess: (created) => onOpen(created.id) },
                )
              }
              disabled={start.isPending}
              className="border border-accent px-4 py-2 font-mono text-xs uppercase tracking-[0.14em] text-accent hover:bg-accent hover:text-bg disabled:opacity-40"
            >
              {start.isPending ? t('games.dealing') : t('games.play')}
            </button>
            <p className="font-mono text-xs text-muted">
              {t('guess.summonsAvailable', { count: availability.data?.summonsAvailable ?? 0 })}
            </p>
          </div>
        ) : reason !== null ? (
          // The honest empty state, per blocker. Every one of these is a fact about the player's own
          // grimoire, and every one has the same cure — which is why the cure is spelled out.
          <div className="border border-dashed border-line p-6">
            <p className="max-w-prose font-body text-sm text-muted">
              {t(`guess.blocked.${reason}`, { count: availability.data?.summonsAvailable ?? 0 })}
            </p>
            <p className="mt-2 font-mono text-xs text-faint">{t('guess.blockedCure')}</p>
          </div>
        ) : null}

        {start.isError ? (
          <p className="font-mono text-sm text-danger">{t('games.startError')}</p>
        ) : null}
      </div>
    </div>
  );
}

function DifficultyButton({
  active,
  onClick,
  title,
  body,
}: {
  active: boolean;
  onClick: () => void;
  title: string;
  body: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`border p-4 text-left ${
        active ? 'border-accent bg-panel' : 'border-line hover:border-accent'
      }`}
    >
      <span className={`block font-display text-lg ${active ? 'text-accent' : 'text-strong'}`}>
        {title}
      </span>
      <span className="mt-1 block font-body text-xs text-muted">{body}</span>
    </button>
  );
}

// One friend, with whether they can actually be played and — when they cannot — the honest reason
// why. Every reason is a different sentence about real data: "they have not allowed it" is not the
// same fact as "they have never banished anything", and collapsing them into a greyed-out button
// would tell the player nothing.
function FriendRow({
  friend,
  onPlay,
  starting,
}: {
  friend: Friend;
  onPlay: () => void;
  starting: boolean;
}) {
  const { t } = useTranslation();
  const availability = useVerdictGameAvailability(friend.userId);

  const handle = friend.handle !== null ? `@${friend.handle}` : t('games.aFriend');
  const playable = availability.data?.playable === true;
  const reason = availability.data?.reason ?? null;

  return (
    <li className="flex flex-wrap items-center justify-between gap-3 py-4">
      <div className="min-w-0">
        <p className="font-body font-medium text-strong">{handle}</p>
        {availability.isLoading ? (
          <p className="mt-1 font-mono text-xs text-faint">{t('games.checking')}</p>
        ) : playable ? (
          <p className="mt-1 font-mono text-xs text-muted">
            {t('games.verdictsAvailable', { count: availability.data?.verdictsAvailable ?? 0 })}
          </p>
        ) : reason !== null ? (
          <p className="mt-1 max-w-prose font-mono text-xs text-faint">
            {t(`games.blocked.${reason}`, { handle })}
          </p>
        ) : null}
      </div>

      {playable ? (
        <button
          type="button"
          onClick={onPlay}
          disabled={starting}
          className="border border-accent px-4 py-2 font-mono text-xs uppercase tracking-[0.14em] text-accent hover:bg-accent hover:text-bg disabled:opacity-40"
        >
          {starting ? t('games.dealing') : t('games.play')}
        </button>
      ) : null}
    </li>
  );
}

// The consent this game needs to exist. Stated plainly, because what it allows is plain: a friend
// finding out that you BANISHED a band. Nothing else in the app has ever shown that to anyone but
// you (the Mirror, C20, is yours alone).
function ConsentPanel() {
  const { t } = useTranslation();
  const consent = useVerdictGameConsent(true);
  const setConsent = useSetVerdictGameConsent();

  if (consent.isLoading || consent.data === undefined) {
    return null;
  }

  const optIn = consent.data.optIn;

  return (
    <div className="border border-line p-6">
      <h2 className="font-mono text-xs uppercase tracking-[0.2em] text-muted">
        {t('games.consentTitle')}
      </h2>
      <p className="mt-2 max-w-prose font-body text-sm text-muted">{t('games.consentBody')}</p>

      <div className="mt-4 flex flex-wrap items-center gap-3">
        <button
          type="button"
          onClick={() => setConsent.mutate(optIn !== true)}
          disabled={setConsent.isPending}
          className={`border px-4 py-2 font-mono text-xs uppercase tracking-[0.14em] disabled:opacity-40 ${
            optIn === true
              ? 'border-accent bg-accent text-bg'
              : 'border-line text-muted hover:border-accent hover:text-accent'
          }`}
        >
          {optIn === true ? t('games.consentOn') : t('games.consentOff')}
        </button>

        {/* Null is not false: never asked is a different fact from asked and declined (D61's lesson). */}
        <p className="font-mono text-xs text-faint">
          {optIn === null
            ? t('games.consentNeverAsked')
            : optIn
              ? t('games.consentAllowed')
              : t('games.consentDeclined')}
        </p>
      </div>
    </div>
  );
}

// Both sides of the turn, for guess-the-band — and the one place two scores over two DIFFERENT
// grimoires can be read next to each other, which is the whole of what "against a friend" means here.
//
// The difficulty is on every row, and it has to be: 5/5 typed and 5/5 picked are not the same feat,
// and a list that showed only the fraction would be inviting a comparison it had already broken. The
// points are the server's arithmetic, not this component's — two clients each doing their own would
// eventually disagree about a number two friends are arguing over.
function GuessHistory({
  games,
  onOpen,
}: {
  games: GuessGameSummary[];
  onOpen: (id: string) => void;
}) {
  const { t } = useTranslation();

  if (games.length === 0) {
    return null;
  }

  return (
    <div className="space-y-4">
      <h2 className="font-mono text-xs uppercase tracking-[0.2em] text-muted">
        {t('guess.historyTitle')}
      </h2>

      <ul className="divide-y divide-line border-y border-line">
        {games.map((game) => {
          const handle = game.otherHandle !== null ? `@${game.otherHandle}` : t('guess.aFriend');

          return (
            <li key={game.id} className="flex flex-wrap items-center justify-between gap-3 py-4">
              <div className="min-w-0">
                <p className="font-body text-strong">
                  {game.otherUserId === null
                    ? t('guess.historySolo')
                    : game.playedByMe
                      ? t('guess.historyMine', { handle })
                      : t('guess.historyTheirs', { handle })}
                </p>
                <p className="mt-1 font-mono text-xs text-muted">
                  {game.createdAt.slice(0, 10)} ·{' '}
                  {game.difficulty === 'Hard' ? t('guess.hardTitle') : t('guess.normalTitle')} ·{' '}
                  {game.status === 'Finished'
                    ? t('guess.historyScore', {
                        correct: game.score.correct,
                        total: game.score.total,
                        points: game.score.points,
                      })
                    : t('guess.historyUnfinished', {
                        answered: game.score.answered,
                        total: game.score.total,
                      })}
                </p>
              </div>

              {/* Only your own games are openable: a game belongs to whoever was dealt it. A friend's
                  challenge is a score to beat, not a game you can play their rounds of. */}
              {game.playedByMe ? (
                <button
                  type="button"
                  onClick={() => onOpen(game.id)}
                  className="border border-line px-3 py-1.5 font-mono text-xs uppercase tracking-[0.14em] text-muted hover:border-accent hover:text-accent"
                >
                  {game.status === 'Finished' ? t('games.review') : t('games.resume')}
                </button>
              ) : null}
            </li>
          );
        })}
      </ul>
    </div>
  );
}

// Both sides of the turn. A game played AGAINST you is the one you reply to — that is the whole
// loop, and it lives here rather than in a state machine.
function GameHistory({
  games,
  onOpen,
}: {
  games: VerdictGameSummary[];
  onOpen: (id: string) => void;
}) {
  const { t } = useTranslation();

  if (games.length === 0) {
    return null;
  }

  return (
    <div className="space-y-4">
      <h2 className="font-mono text-xs uppercase tracking-[0.2em] text-muted">
        {t('games.historyTitle')}
      </h2>

      <ul className="divide-y divide-line border-y border-line">
        {games.map((game) => {
          const handle = game.otherHandle !== null ? `@${game.otherHandle}` : t('games.aFriend');

          return (
            <li key={game.id} className="flex flex-wrap items-center justify-between gap-3 py-4">
              <div className="min-w-0">
                <p className="font-body text-strong">
                  {game.playedByMe
                    ? t('games.historyMine', { handle })
                    : t('games.historyTheirs', { handle })}
                </p>
                <p className="mt-1 font-mono text-xs text-muted">
                  {game.createdAt.slice(0, 10)} ·{' '}
                  {game.status === 'Finished'
                    ? t('games.historyScore', {
                        correct: game.score.correct,
                        total: game.score.total,
                      })
                    : t('games.historyUnfinished', {
                        answered: game.score.answered,
                        total: game.score.total,
                      })}
                </p>
              </div>

              {/* Only your own games are openable: a game belongs to whoever was dealt it. */}
              {game.playedByMe ? (
                <button
                  type="button"
                  onClick={() => onOpen(game.id)}
                  className="border border-line px-3 py-1.5 font-mono text-xs uppercase tracking-[0.14em] text-muted hover:border-accent hover:text-accent"
                >
                  {game.status === 'Finished' ? t('games.review') : t('games.resume')}
                </button>
              ) : null}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
