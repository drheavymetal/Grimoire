import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import {
  useMarkAllRead,
  useMarkRead,
  useNotifications,
} from '../../core/hooks/useNotifications';
import type { Notification } from '../../core/domain/types';
import { useAuth } from '../auth/AuthProvider';
import { AuthPanel } from '../auth/AuthPanel';
import { PageHeader } from '../PageHeader';

// The NOTIFICATIONS wave — the inbox page. A polled list (not web push): friend requests, accepted
// requests, and blind gifts. Each item reads in the app's voice and links to where the action lives
// (Friends for the friend events, the blind gift flow for a gift). A gift stays BLIND here: the list
// never shows the band name, only "someone sent you a blind gift". Opening an unread item marks it
// read; "mark all as read" clears the sidebar badge. Authenticated, gated by the AuthPanel like the
// grimoire, mirror, profile and friends pages.
export function NotificationsPage() {
  const { t } = useTranslation();
  const { status, isAuthenticated } = useAuth();

  if (status === 'unknown') {
    return <p className="font-mono text-sm text-muted">{t('rite.checking')}</p>;
  }

  if (!isAuthenticated) {
    return <AuthPanel />;
  }

  return <NotificationsBody />;
}

function NotificationsBody() {
  const { t } = useTranslation();
  const list = useNotifications(true);
  const markAll = useMarkAllRead();

  const items = list.data ?? [];
  const hasUnread = items.some((item) => !item.read);

  return (
    <section className="space-y-8">
      <PageHeader
        eyebrow={t('notifications.eyebrow')}
        title={t('notifications.heading')}
        aside={
          hasUnread ? (
            <button
              type="button"
              onClick={() => markAll.mutate()}
              disabled={markAll.isPending}
              className="border border-line px-3 py-1.5 font-mono text-xs uppercase tracking-[0.14em] text-muted hover:border-accent hover:text-accent disabled:opacity-50"
            >
              {markAll.isPending ? t('notifications.marking') : t('notifications.markAll')}
            </button>
          ) : undefined
        }
      />

      {list.isLoading ? (
        <p className="font-mono text-sm text-muted">{t('notifications.loading')}</p>
      ) : list.isError ? (
        <p className="font-mono text-sm text-danger">{t('notifications.error')}</p>
      ) : items.length === 0 ? (
        <div className="border border-line border-dashed p-8 text-center">
          <p className="font-display text-xl text-strong">{t('notifications.emptyTitle')}</p>
          <p className="mx-auto mt-2 max-w-prose font-body text-sm text-muted">
            {t('notifications.emptyBody')}
          </p>
        </div>
      ) : (
        <ul className="divide-y divide-line border-y border-line">
          {items.map((item) => (
            <NotificationRow key={item.id} notification={item} />
          ))}
        </ul>
      )}
    </section>
  );
}

// One inbox row. The words and the link target come from the type; the band name of a GiftReceived
// is NEVER rendered — the gift is blind until the recipient opens the blind flow. Opening an unread
// row marks it read.
function NotificationRow({ notification }: { notification: Notification }) {
  const { t } = useTranslation();
  const markRead = useMarkRead();

  const handle =
    notification.actorHandle !== null ? `@${notification.actorHandle}` : t('notifications.someone');

  const message =
    notification.type === 'FriendRequest'
      ? t('notifications.friendRequest', { handle })
      : notification.type === 'FriendAccepted'
        ? t('notifications.friendAccepted', { handle })
        : notification.type === 'RaritySurpassed'
          ? t('notifications.raritySurpassed', { handle })
          : notification.type === 'DuelChallenge'
            ? t('notifications.duelChallenge', { handle })
            : notification.type === 'VerdictGamePlayed'
              ? // Their score is the message: it is the invitation to play back.
                t('notifications.verdictGamePlayed', {
                  handle,
                  correct: notification.scoreCorrect ?? 0,
                  total: notification.scoreTotal ?? 0,
                })
              : t('notifications.giftReceived', { handle });

  const action =
    notification.type === 'GiftReceived'
      ? notification.giftToken !== null
        ? t('notifications.openGift')
        : null
      : notification.type === 'VerdictGamePlayed'
        ? t('notifications.openGames')
        : t('notifications.openFriends');

  function markIfUnread() {
    if (!notification.read) {
      markRead.mutate(notification.id);
    }
  }

  const body = (
    <>
      <span
        aria-hidden="true"
        className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ${
          notification.read ? 'bg-transparent' : 'bg-accent'
        }`}
      />
      <span className="min-w-0 flex-1">
        <span
          className={`block font-body ${notification.read ? 'text-muted' : 'font-medium text-strong'}`}
        >
          {message}
        </span>
        <span className="mt-1 flex flex-wrap items-baseline gap-x-3 font-mono text-xs text-muted">
          <span>{notification.createdAt.slice(0, 10)}</span>
          {action !== null ? <span className="text-accent">{action} →</span> : null}
        </span>
      </span>
    </>
  );

  const rowClass = 'flex items-start gap-3 py-4 text-left no-underline';

  // A gift links to the existing blind flow; the friend events link to the Friends page. A gift with
  // no token cannot be opened, so it renders as a plain read-marking button rather than a dead link.
  if (notification.type === 'GiftReceived' && notification.giftToken !== null) {
    return (
      <li>
        <Link
          to="/gift/$token"
          params={{ token: notification.giftToken }}
          onClick={markIfUnread}
          className={`${rowClass} block hover:bg-panel`}
        >
          {body}
        </Link>
      </li>
    );
  }

  // A played verdict game hands the turn over: it links to the games page, where the reply is a
  // game started back against them.
  if (notification.type === 'VerdictGamePlayed') {
    return (
      <li>
        <Link to="/games" onClick={markIfUnread} className={`${rowClass} block hover:bg-panel`}>
          {body}
        </Link>
      </li>
    );
  }

  // The friend events and the two rarity/duel events all live on the Friends page: the leaderboard
  // (where a rarity pass shows) and the duel face-off both open from there.
  if (
    notification.type === 'FriendRequest' ||
    notification.type === 'FriendAccepted' ||
    notification.type === 'RaritySurpassed' ||
    notification.type === 'DuelChallenge'
  ) {
    return (
      <li>
        <Link to="/friends" onClick={markIfUnread} className={`${rowClass} block hover:bg-panel`}>
          {body}
        </Link>
      </li>
    );
  }

  return (
    <li>
      <button type="button" onClick={markIfUnread} className={`${rowClass} w-full hover:bg-panel`}>
        {body}
      </button>
    </li>
  );
}
