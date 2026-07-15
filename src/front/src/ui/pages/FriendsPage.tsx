import { useMemo, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../core/api/client';
import {
  useAcceptFriend,
  useBlockUser,
  useDeclineFriend,
  useChallengeDuel,
  useFriendAtlasPoint,
  useFriendCrossed,
  useFriendDuel,
  useFriendGrimoire,
  useFriendRequests,
  useFriends,
  useGiftToFriend,
  useLeaderboard,
  useRemoveFriend,
  useRequestFriend,
} from '../../core/hooks/useFriends';
import { useArtistSearch } from '../../core/hooks/useArtistSearch';
import { useDebouncedValue } from '../../core/hooks/useDebouncedValue';
import { useAtlas } from '../../core/hooks/useAtlas';
import { starsNearTaste } from '../../core/domain/atlas';
import type {
  ArtistSummary,
  Friend,
  FriendRequest,
  GrimoireEntry,
  LeaderboardEntry,
} from '../../core/domain/types';
import { useAuth } from '../auth/AuthProvider';
import { AuthPanel } from '../auth/AuthPanel';
import { AtlasCanvas } from '../atlas/AtlasCanvas';
import { PageHeader } from '../PageHeader';
import { RankedName } from '../RankedName';
import { SectionHead } from '../SectionHead';

// How many stars around the taste burn alive on the friend mini-Atlas. Mirrors AtlasPage ALIVE_COUNT.
const ALIVE_COUNT = 28;

// A friend's public handle rendered as @handle, or a fallback when they have not set one yet.
function handleLabel(handle: string | null, fallback: string): string {
  return handle !== null ? `@${handle}` : fallback;
}

// The Friends page (the FRIENDS wave): add friends by handle, work the request queue, and then dig
// into a friend's grimoire, cross grimoires, or place them on the Atlas. Rarity is the sport — the
// leaderboard ranks who has dug deepest by Depth Score. Authenticated, gated by the AuthPanel like
// the grimoire, mirror and profile pages.
export function FriendsPage() {
  const { t } = useTranslation();
  const { status, isAuthenticated } = useAuth();

  if (status === 'unknown') {
    return <p className="font-mono text-sm text-muted">{t('rite.checking')}</p>;
  }

  if (!isAuthenticated) {
    return <AuthPanel />;
  }

  return <FriendsBody />;
}

// What the user has opened under a friend: their grimoire, the cross, the Atlas, the gift picker,
// or the taste duel. Null = closed.
type FriendView = 'grimoire' | 'crossed' | 'atlas' | 'gift' | 'duel';

interface Selection {
  friend: Friend;
  view: FriendView;
}

function FriendsBody() {
  const { t } = useTranslation();
  const [selection, setSelection] = useState<Selection | null>(null);

  function open(friend: Friend, view: FriendView) {
    setSelection((prev) =>
      prev !== null && prev.friend.userId === friend.userId && prev.view === view
        ? null
        : { friend, view },
    );
  }

  return (
    <section className="space-y-12">
      <PageHeader eyebrow={t('friends.eyebrow')} title={t('friends.heading')} />

      <AddFriend />
      <Requests />
      <FriendsList selection={selection} onOpen={open} />
      {selection !== null ? (
        <FriendDetail
          selection={selection}
          onClose={() => setSelection(null)}
        />
      ) : null}
      <Leaderboard />
    </section>
  );
}

// Add a friend by their handle. 404 (unknown), 400 (adding yourself), 409 (already friends/pending)
// each get their own friendly line; anything else is the generic error.
function AddFriend() {
  const { t } = useTranslation();
  const [handle, setHandle] = useState('');
  const request = useRequestFriend();

  function submit(event: React.FormEvent) {
    event.preventDefault();
    const value = handle.trim().replace(/^@/, '').toLowerCase();
    if (value.length === 0) {
      return;
    }
    request.mutate(value, {
      onSuccess: () => {
        setHandle('');
      },
    });
  }

  const status = request.error instanceof ApiError ? request.error.status : null;
  const errorKey =
    status === 404
      ? 'friends.addUnknown'
      : status === 400
        ? 'friends.addSelf'
        : status === 409
          ? 'friends.addDuplicate'
          : request.isError
            ? 'friends.addError'
            : null;

  return (
    <section>
      <SectionHead title={t('friends.addTitle')} hint={t('friends.addHint')} />

      <form onSubmit={submit} className="mt-4 flex flex-wrap gap-3">
        <label className="flex min-w-0 flex-1 items-center gap-1 border border-line bg-panel px-3 py-2 focus-within:border-accent">
          <span className="font-mono text-sm text-muted">@</span>
          <input
            type="text"
            value={handle}
            onChange={(event) => setHandle(event.target.value)}
            placeholder={t('friends.addPlaceholder')}
            autoComplete="off"
            className="min-w-0 flex-1 bg-transparent font-body text-strong outline-none"
          />
        </label>
        <button
          type="submit"
          disabled={request.isPending || handle.trim().length === 0}
          className="border border-accent px-5 py-2 font-mono text-xs uppercase tracking-[0.18em] text-accent hover:bg-accent hover:text-bg disabled:opacity-50"
        >
          {request.isPending ? t('friends.adding') : t('friends.add')}
        </button>
      </form>

      {errorKey !== null ? (
        <p className="mt-2 font-mono text-sm text-danger">{t(errorKey)}</p>
      ) : request.isSuccess ? (
        <p className="mt-2 font-mono text-sm text-strong">{t('friends.addDone')}</p>
      ) : null}
    </section>
  );
}

// The pending queue: incoming requests (accept / decline) and your outgoing ones (pending), each
// with a count so the size of the queue is legible at a glance.
function Requests() {
  const { t } = useTranslation();
  const requests = useFriendRequests(true);
  const accept = useAcceptFriend();
  const decline = useDeclineFriend();

  const incoming = requests.data?.incoming ?? [];
  const outgoing = requests.data?.outgoing ?? [];

  return (
    <section>
      <SectionHead title={t('friends.requestsTitle')} hint={t('friends.requestsHint')} />

      {requests.isLoading ? (
        <p className="mt-3 font-mono text-sm text-muted">{t('friends.loading')}</p>
      ) : requests.isError ? (
        <p className="mt-3 font-mono text-sm text-danger">{t('friends.error')}</p>
      ) : incoming.length === 0 && outgoing.length === 0 ? (
        <p className="mt-3 max-w-prose font-body text-sm text-muted">{t('friends.requestsEmpty')}</p>
      ) : (
        <div className="mt-4 space-y-6">
          <div>
            <h3 className="font-mono text-xs uppercase text-muted">
              {t('friends.incoming', { n: incoming.length })}
            </h3>
            {incoming.length === 0 ? (
              <p className="mt-2 font-mono text-xs text-muted">{t('friends.incomingEmpty')}</p>
            ) : (
              <ul className="mt-2 divide-y divide-line border-y border-line">
                {incoming.map((req) => (
                  <IncomingRow
                    key={req.friendshipId}
                    request={req}
                    onAccept={() => accept.mutate(req.friendshipId)}
                    onDecline={() => decline.mutate(req.friendshipId)}
                    busy={accept.isPending || decline.isPending}
                  />
                ))}
              </ul>
            )}
          </div>

          <div>
            <h3 className="font-mono text-xs uppercase text-muted">
              {t('friends.outgoing', { n: outgoing.length })}
            </h3>
            {outgoing.length === 0 ? (
              <p className="mt-2 font-mono text-xs text-muted">{t('friends.outgoingEmpty')}</p>
            ) : (
              <ul className="mt-2 divide-y divide-line border-y border-line">
                {outgoing.map((req) => (
                  <li
                    key={req.friendshipId}
                    className="flex items-baseline justify-between gap-4 py-3"
                  >
                    <span className="font-body text-strong">
                      {handleLabel(req.handle, t('friends.noHandle'))}
                    </span>
                    <span className="shrink-0 font-mono text-xs text-muted">
                      {t('friends.pending')}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}
    </section>
  );
}

function IncomingRow({
  request,
  onAccept,
  onDecline,
  busy,
}: {
  request: FriendRequest;
  onAccept: () => void;
  onDecline: () => void;
  busy: boolean;
}) {
  const { t } = useTranslation();

  return (
    <li className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-2 py-3">
      <span className="font-body text-strong">{handleLabel(request.handle, t('friends.noHandle'))}</span>
      <span className="flex gap-2">
        <button
          type="button"
          onClick={onAccept}
          disabled={busy}
          className="border border-accent px-3 py-1.5 font-mono text-xs uppercase tracking-[0.14em] text-accent hover:bg-accent hover:text-bg disabled:opacity-50"
        >
          {t('friends.accept')}
        </button>
        <button
          type="button"
          onClick={onDecline}
          disabled={busy}
          className="border border-line px-3 py-1.5 font-mono text-xs uppercase tracking-[0.14em] text-muted hover:text-danger disabled:opacity-50"
        >
          {t('friends.decline')}
        </button>
      </span>
    </li>
  );
}

// The confirmed friends. Each row carries the rarity numbers and the door actions — view grimoire,
// cross grimoires, see on Atlas — plus remove and block.
function FriendsList({
  selection,
  onOpen,
}: {
  selection: Selection | null;
  onOpen: (friend: Friend, view: FriendView) => void;
}) {
  const { t } = useTranslation();
  const friends = useFriends(true);
  const remove = useRemoveFriend();
  const block = useBlockUser();

  const list = friends.data ?? [];

  return (
    <section>
      <SectionHead title={t('friends.listTitle')} hint={t('friends.listHint')} />

      {friends.isLoading ? (
        <p className="mt-3 font-mono text-sm text-muted">{t('friends.loading')}</p>
      ) : friends.isError ? (
        <p className="mt-3 font-mono text-sm text-danger">{t('friends.error')}</p>
      ) : list.length === 0 ? (
        <div className="mt-4 border border-line p-6">
          <p className="font-display text-xl text-strong">{t('friends.listEmptyTitle')}</p>
          <p className="mt-2 max-w-prose font-body text-sm text-muted">{t('friends.listEmptyBody')}</p>
        </div>
      ) : (
        <ul className="mt-4 space-y-3">
          {list.map((friend) => {
            const active = selection?.friend.userId === friend.userId ? selection.view : null;
            return (
              <li key={friend.friendshipId} className="border border-line p-4">
                <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
                  <span className="font-display text-xl text-strong">
                    {handleLabel(friend.handle, t('friends.noHandle'))}
                  </span>
                  <span className="shrink-0 font-mono text-xs text-muted">
                    {t('friends.depth', { depth: friend.depthScore })}
                    {' · '}
                    {t('friends.summoned', { n: friend.summonedCount })}
                  </span>
                </div>

                <div className="mt-3 flex flex-wrap gap-x-4 gap-y-2">
                  <FriendAction
                    label={t('friends.viewGrimoire')}
                    active={active === 'grimoire'}
                    onClick={() => onOpen(friend, 'grimoire')}
                  />
                  <FriendAction
                    label={t('friends.viewCrossed')}
                    active={active === 'crossed'}
                    onClick={() => onOpen(friend, 'crossed')}
                  />
                  <FriendAction
                    label={t('friends.viewAtlas')}
                    active={active === 'atlas'}
                    onClick={() => onOpen(friend, 'atlas')}
                  />
                  <FriendAction
                    label={t('friends.giftAction')}
                    active={active === 'gift'}
                    onClick={() => onOpen(friend, 'gift')}
                  />
                  <FriendAction
                    label={t('friends.duelAction')}
                    active={active === 'duel'}
                    onClick={() => onOpen(friend, 'duel')}
                  />
                  <button
                    type="button"
                    onClick={() => remove.mutate(friend.friendshipId)}
                    disabled={remove.isPending}
                    className="font-mono text-xs uppercase tracking-[0.14em] text-muted hover:text-danger disabled:opacity-50"
                  >
                    {t('friends.remove')}
                  </button>
                  <button
                    type="button"
                    onClick={() => block.mutate(friend.userId)}
                    disabled={block.isPending}
                    className="font-mono text-xs uppercase tracking-[0.14em] text-muted hover:text-danger disabled:opacity-50"
                  >
                    {t('friends.block')}
                  </button>
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

function FriendAction({
  label,
  active,
  onClick,
}: {
  label: string;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`font-mono text-xs uppercase tracking-[0.14em] no-underline ${
        active ? 'text-strong underline decoration-accent underline-offset-4' : 'text-accent hover:text-strong'
      }`}
    >
      {label}
    </button>
  );
}

// The opened panel under a friend: their grimoire, the cross, or the Atlas overlay. Each fetches on
// mount only (so nothing loads until a door is opened) and closes back to the list.
function FriendDetail({ selection, onClose }: { selection: Selection; onClose: () => void }) {
  const { t } = useTranslation();
  const { friend, view } = selection;
  const name = handleLabel(friend.handle, t('friends.noHandle'));

  return (
    <section className="border border-accent p-5">
      <div className="flex flex-wrap items-baseline justify-between gap-3">
        <h2 className="font-display text-2xl text-strong">
          {view === 'grimoire'
            ? t('friends.detailGrimoire', { name })
            : view === 'crossed'
              ? t('friends.detailCrossed', { name })
              : view === 'atlas'
                ? t('friends.detailAtlas', { name })
                : view === 'gift'
                  ? t('friends.detailGift', { name })
                  : t('friends.detailDuel', { name })}
        </h2>
        <button
          type="button"
          onClick={onClose}
          className="font-mono text-xs uppercase tracking-[0.14em] text-muted hover:text-strong"
        >
          {t('friends.close')}
        </button>
      </div>

      <div className="mt-5">
        {view === 'grimoire' ? (
          <FriendGrimoireView friend={friend} />
        ) : view === 'crossed' ? (
          <FriendCrossedView friend={friend} />
        ) : view === 'atlas' ? (
          <FriendAtlasView friend={friend} name={name} />
        ) : view === 'gift' ? (
          <FriendGiftView friend={friend} />
        ) : (
          <FriendDuelView friend={friend} name={name} />
        )}
      </div>
    </section>
  );
}

// A friend's grimoire, reusing the same list shape as the caller's own grimoire page.
function FriendGrimoireView({ friend }: { friend: Friend }) {
  const { t } = useTranslation();
  const grimoire = useFriendGrimoire(friend.userId);

  if (grimoire.isLoading) {
    return <p className="font-mono text-sm text-muted">{t('friends.loading')}</p>;
  }
  if (grimoire.isError) {
    return <p className="font-mono text-sm text-danger">{t('friends.forbidden')}</p>;
  }

  const entries = grimoire.data ?? [];
  if (entries.length === 0) {
    return <p className="max-w-prose font-body text-sm text-muted">{t('friends.grimoireEmpty')}</p>;
  }

  return (
    <ul className="divide-y divide-line border-y border-line">
      {entries.map((entry: GrimoireEntry) => (
        <li key={entry.artist.id}>
          <Link
            to="/artist/$artistId"
            params={{ artistId: entry.artist.id }}
            className="flex items-baseline justify-between gap-4 py-3 no-underline"
          >
            <span className="font-display text-lg text-strong">{entry.artist.name}</span>
            <span className="shrink-0 font-mono text-xs text-muted">
              {entry.artist.country ?? t('search.countryUnknown')}
              {' · '}
              {t('grimoire.summonedOn', { date: entry.resolvedAt.slice(0, 10) })}
            </span>
          </Link>
        </li>
      ))}
    </ul>
  );
}

// The cross of the two grimoires, reusing the three-column layout of the crossed-grimoires UI.
function FriendCrossedView({ friend }: { friend: Friend }) {
  const { t } = useTranslation();
  const cross = useFriendCrossed(friend.userId);

  if (cross.isLoading) {
    return <p className="font-mono text-sm text-muted">{t('friends.crossing')}</p>;
  }
  if (cross.isError) {
    return <p className="font-mono text-sm text-danger">{t('friends.forbidden')}</p>;
  }
  if (cross.data === undefined) {
    return null;
  }

  return (
    <div className="space-y-6">
      <CrossColumn title={t('crossed.theirsOnly')} empty={t('crossed.theirsEmpty')} bands={cross.data.theirsOnly} accent />
      <CrossColumn title={t('crossed.shared')} empty={t('crossed.sharedEmpty')} bands={cross.data.shared} />
      <CrossColumn title={t('crossed.yoursOnly')} empty={t('crossed.yoursEmpty')} bands={cross.data.yoursOnly} />
    </div>
  );
}

function CrossColumn({
  title,
  empty,
  bands,
  accent = false,
}: {
  title: string;
  empty: string;
  bands: ArtistSummary[];
  accent?: boolean;
}) {
  return (
    <div>
      <h3 className={`font-mono text-xs uppercase ${accent ? 'text-accent' : 'text-muted'}`}>{title}</h3>
      {bands.length === 0 ? (
        <p className="mt-1 font-mono text-xs text-muted">{empty}</p>
      ) : (
        <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1">
          {bands.map((band) => (
            <li key={band.id}>
              <Link
                to="/artist/$artistId"
                params={{ artistId: band.id }}
                className="font-body text-strong no-underline hover:text-accent"
              >
                {band.name}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// The friend placed on the star map: the full Atlas with the friend's taste overlaid as a distinct
// danger-coloured diamond (a legend entry names them). Reuses the AtlasCanvas hover/pin intact.
function FriendAtlasView({ friend, name }: { friend: Friend; name: string }) {
  const { t } = useTranslation();
  const atlas = useAtlas(true);
  const point = useFriendAtlasPoint(friend.userId);

  const aliveIds = useMemo(
    () => starsNearTaste(atlas.data?.stars ?? [], atlas.data?.taste ?? null, ALIVE_COUNT),
    [atlas.data?.stars, atlas.data?.taste],
  );

  if (atlas.isLoading) {
    return <p className="font-mono text-sm text-muted">{t('atlas.loading')}</p>;
  }
  if (atlas.isError || atlas.data === undefined) {
    return <p className="font-mono text-sm text-danger">{t('atlas.error')}</p>;
  }

  const px = point.data?.x ?? null;
  const py = point.data?.y ?? null;
  const friendPoint = px !== null && py !== null ? { x: px, y: py } : null;
  const hasPoint = friendPoint !== null;

  return (
    <>
      {point.isError ? (
        <p className="font-mono text-sm text-danger">{t('friends.forbidden')}</p>
      ) : !point.isLoading && !hasPoint ? (
        <p className="max-w-prose font-body text-sm text-muted">{t('friends.atlasNoTaste')}</p>
      ) : null}
      <AtlasCanvas atlas={atlas.data} aliveIds={aliveIds} friendPoint={friendPoint} friendLabel={name} />
    </>
  );
}

// Send a friend a blind gift (the NOTIFICATIONS wave). A debounced band search (the same typeahead
// as the profile's anchor box), pick a band, and it lands face down in their inbox. It STAYS BLIND
// for the recipient — they hear it and only then turn it over — so the confirmation never implies
// they can see the name (the sender obviously knows what they picked). 403 (not friends) and 404
// (band missing) surface as friendly copy.
function FriendGiftView({ friend }: { friend: Friend }) {
  const { t } = useTranslation();
  const [term, setTerm] = useState('');
  const [sent, setSent] = useState(false);
  const debounced = useDebouncedValue(term, 300);
  const search = useArtistSearch(debounced);
  const gift = useGiftToFriend();

  const showResults = debounced.trim().length >= 2;
  const suggestions = search.data ?? [];

  function pick(artist: ArtistSummary) {
    gift.mutate(
      { friendId: friend.userId, artistId: artist.id },
      {
        onSuccess: () => {
          setSent(true);
          setTerm('');
        },
      },
    );
  }

  const status = gift.error instanceof ApiError ? gift.error.status : null;
  const errorKey =
    status === 403
      ? 'friends.giftForbidden'
      : status === 404
        ? 'friends.giftMissing'
        : gift.isError
          ? 'friends.giftError'
          : null;

  if (sent) {
    return (
      <div>
        <p className="font-display text-xl text-strong">{t('friends.giftSent')}</p>
        <p className="mt-2 max-w-prose font-body text-sm text-muted">{t('friends.giftSentHint')}</p>
        <button
          type="button"
          onClick={() => {
            gift.reset();
            setSent(false);
          }}
          className="mt-4 border border-line px-4 py-2 font-mono text-xs uppercase tracking-[0.14em] text-strong hover:border-accent hover:text-accent"
        >
          {t('friends.giftAnother')}
        </button>
      </div>
    );
  }

  return (
    <div>
      <p className="max-w-prose font-body text-sm text-muted">{t('friends.giftIntro')}</p>

      <label className="mt-4 block">
        <span className="font-mono text-xs uppercase text-muted">{t('friends.giftSearchLabel')}</span>
        <input
          type="search"
          value={term}
          onChange={(event) => setTerm(event.target.value)}
          placeholder={t('friends.giftPlaceholder')}
          autoComplete="off"
          className="mt-1 w-full border border-line bg-panel px-4 py-3 font-body text-strong outline-none focus:border-accent"
        />
      </label>

      {errorKey !== null ? (
        <p className="mt-2 font-mono text-xs text-danger">{t(errorKey)}</p>
      ) : null}

      {showResults && search.isFetching ? (
        <p className="mt-2 font-mono text-xs text-muted">{t('friends.giftSearching')}</p>
      ) : null}

      {showResults && !search.isFetching && suggestions.length === 0 ? (
        <p className="mt-2 font-mono text-xs text-muted">{t('friends.giftEmpty')}</p>
      ) : null}

      {suggestions.length > 0 ? (
        <ul className="mt-2 divide-y divide-line border-y border-line">
          {suggestions.map((artist) => (
            <li key={artist.id}>
              <button
                type="button"
                onClick={() => pick(artist)}
                disabled={gift.isPending}
                className="flex w-full items-baseline justify-between gap-4 py-2.5 text-left disabled:opacity-50"
              >
                <RankedName name={artist.name} rank={artist.rank} className="font-body text-strong" />
                <span className="shrink-0 font-mono text-xs text-muted">
                  {gift.isPending ? t('friends.giftSending') : artist.country ?? t('search.countryUnknown')}
                </span>
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

// A light taste duel with a friend (the NOTIFICATIONS wave). Head-to-head Depth Scores (rarer wins,
// so it reads as "who has dug deepest"), the crossed counts of the two grimoires, and how the two
// tastes align. The Challenge button drops a duel notification on the friend's side. 403 (not
// friends) surfaces as friendly copy for both the read and the challenge.
function FriendDuelView({ friend, name }: { friend: Friend; name: string }) {
  const { t } = useTranslation();
  const duel = useFriendDuel(friend.userId, true);
  const challenge = useChallengeDuel();

  if (duel.isLoading) {
    return <p className="font-mono text-sm text-muted">{t('friends.duelLoading')}</p>;
  }
  if (duel.isError || duel.data === undefined) {
    const status = duel.error instanceof ApiError ? duel.error.status : null;
    return (
      <p className="font-mono text-sm text-danger">
        {status === 403 ? t('friends.forbidden') : t('friends.duelError')}
      </p>
    );
  }

  const result = duel.data;
  const myWins = result.winner === 'me';
  const theirWins = result.winner === 'them';
  const alignmentPct = result.alignment !== null ? Math.round(result.alignment * 100) : null;

  const challengeStatus = challenge.error instanceof ApiError ? challenge.error.status : null;

  return (
    <div className="space-y-6">
      <p className="max-w-prose font-body text-sm text-muted">{t('friends.duelIntro')}</p>

      <div className="grid grid-cols-[1fr_auto_1fr] items-center gap-3">
        <DuelSide
          label={t('friends.you')}
          depth={result.myDepth}
          won={myWins}
          tie={result.winner === 'tie'}
        />
        <span className="font-mono text-xs uppercase tracking-[0.14em] text-muted">
          {t('friends.duelVersus')}
        </span>
        <DuelSide
          label={name}
          depth={result.theirDepth}
          won={theirWins}
          tie={result.winner === 'tie'}
        />
      </div>

      <p className="text-center font-body text-sm text-strong">
        {myWins
          ? t('friends.duelYouWin')
          : theirWins
            ? t('friends.duelTheyWin', { name })
            : t('friends.duelTie')}
      </p>

      <dl className="grid grid-cols-3 gap-3 border-y border-line py-4 text-center">
        <DuelCount label={t('friends.duelShared')} value={result.shared} />
        <DuelCount label={t('friends.duelMineOnly')} value={result.mineOnly} />
        <DuelCount label={t('friends.duelTheirsOnly')} value={result.theirsOnly} />
      </dl>

      <p className="font-body text-sm text-strong">
        {alignmentPct !== null
          ? t('friends.duelAlignment', { pct: alignmentPct })
          : t('friends.duelNoAlignment')}
      </p>

      {challenge.isSuccess ? (
        <p className="font-mono text-sm text-strong">{t('friends.duelChallengeSent')}</p>
      ) : (
        <div>
          <button
            type="button"
            onClick={() => challenge.mutate(friend.userId)}
            disabled={challenge.isPending}
            className="border border-accent px-5 py-2 font-mono text-xs uppercase tracking-[0.18em] text-accent hover:bg-accent hover:text-bg disabled:opacity-50"
          >
            {challenge.isPending ? t('friends.duelChallenging') : t('friends.duelChallenge')}
          </button>
          {challenge.isError ? (
            <p className="mt-2 font-mono text-xs text-danger">
              {challengeStatus === 403 ? t('friends.forbidden') : t('friends.duelChallengeError')}
            </p>
          ) : null}
        </div>
      )}
    </div>
  );
}

// One competitor in the duel head-to-head: their label and Depth Score, with the winner marked (and
// both marked on a tie). Rarer — a deeper Depth Score — is the winner.
function DuelSide({
  label,
  depth,
  won,
  tie,
}: {
  label: string;
  depth: number;
  won: boolean;
  tie: boolean;
}) {
  const { t } = useTranslation();
  const marked = won || tie;
  return (
    <div className={`border p-4 text-center ${marked ? 'border-accent' : 'border-line'}`}>
      <p className={`truncate font-body ${marked ? 'text-accent' : 'text-strong'}`}>{label}</p>
      <p className="mt-2 font-display text-3xl text-strong">{depth}</p>
      {marked ? (
        <p className="mt-1 font-mono text-xs uppercase tracking-[0.14em] text-accent">
          {tie ? t('friends.duelTieMark') : t('friends.duelWinMark')}
        </p>
      ) : null}
    </div>
  );
}

// One crossed count of the duel: how many bands are shared, only yours, or only theirs.
function DuelCount({ label, value }: { label: string; value: number }) {
  return (
    <div>
      <dd className="font-display text-2xl text-strong">{value}</dd>
      <dt className="mt-1 font-mono text-xs uppercase tracking-[0.12em] text-muted">{label}</dt>
    </div>
  );
}

// The rarity leaderboard: the caller and their friends ranked by Depth Score, deepest first. The
// caller's own row is highlighted so they can see where they stand.
function Leaderboard() {
  const { t } = useTranslation();
  const board = useLeaderboard(true);

  const rows = board.data ?? [];

  return (
    <section>
      <SectionHead title={t('friends.leaderboardTitle')} hint={t('friends.leaderboardHint')} />

      {board.isLoading ? (
        <p className="mt-3 font-mono text-sm text-muted">{t('friends.loading')}</p>
      ) : board.isError ? (
        <p className="mt-3 font-mono text-sm text-danger">{t('friends.error')}</p>
      ) : rows.length === 0 ? (
        <p className="mt-3 max-w-prose font-body text-sm text-muted">{t('friends.leaderboardEmpty')}</p>
      ) : (
        <ol className="mt-4 divide-y divide-line border-y border-line">
          {rows.map((entry: LeaderboardEntry, index) => (
            <li
              key={entry.userId}
              className={`flex items-baseline justify-between gap-4 py-3 ${
                entry.isSelf ? 'bg-panel px-3' : ''
              }`}
            >
              <span className="flex min-w-0 items-baseline gap-3">
                <span className="font-mono text-xs text-muted">{index + 1}</span>
                <span className={`truncate font-body ${entry.isSelf ? 'text-accent' : 'text-strong'}`}>
                  {entry.isSelf
                    ? t('friends.you')
                    : handleLabel(entry.handle, t('friends.noHandle'))}
                </span>
              </span>
              <span className="shrink-0 font-mono text-sm text-strong">
                {t('friends.depth', { depth: entry.depthScore })}
              </span>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
