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
import type { Friend, VerdictGameSummary } from '../../core/domain/types';
import { useAuth } from '../auth/AuthProvider';
import { AuthPanel } from '../auth/AuthPanel';
import { PageHeader } from '../PageHeader';
import { VerdictGameConsole } from '../games/VerdictGameConsole';

// The GAMES wave — "did you summon it, or banish it?". Pick a friend, hear 45 blind seconds of a
// band they judged, and call which way they called it. It is not a music quiz: naming bands rewards
// the canon, and this app is an argument against the canon. It is a test of how well you know one
// person's ear.
//
// Turn-based through the inbox, nothing realtime (D60): you play, they get a notification with your
// score, they play back when they feel like it.
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
  const [gameId, setGameId] = useState<string | null>(null);

  const friends = useFriends(true);
  const games = useVerdictGames(true);
  const game = useVerdictGame(gameId);
  const start = useStartVerdictGame();

  const accepted = (friends.data ?? []).filter((f) => f.status === 'Accepted');

  // A game is open: the console owns the screen.
  if (gameId !== null) {
    if (game.isLoading) {
      return <p className="font-mono text-sm text-muted">{t('games.loading')}</p>;
    }

    if (game.isError || game.data === undefined) {
      return (
        <div className="space-y-4">
          <p className="font-mono text-sm text-danger">{t('games.loadError')}</p>
          <button
            type="button"
            onClick={() => setGameId(null)}
            className="border border-line px-4 py-2 font-mono text-xs uppercase text-muted hover:border-accent hover:text-accent"
          >
            {t('games.backToGames')}
          </button>
        </div>
      );
    }

    return (
      <section className="space-y-8">
        <PageHeader eyebrow={t('games.eyebrow')} title={t('games.heading')} />
        <VerdictGameConsole game={game.data} onExit={() => setGameId(null)} />
      </section>
    );
  }

  return (
    <section className="space-y-8">
      <PageHeader
        eyebrow={t('games.eyebrow')}
        title={t('games.heading')}
        lead={t('games.lead')}
      />

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
                  start.mutate(friend.userId, { onSuccess: (created) => setGameId(created.id) })
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

      <GameHistory games={games.data ?? []} onOpen={setGameId} />
    </section>
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
