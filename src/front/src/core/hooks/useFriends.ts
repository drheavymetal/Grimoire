import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type {
  CrossedGrimoires,
  Friend,
  FriendAtlasPoint,
  FriendDuel,
  FriendRequests,
  GrimoireEntry,
  LeaderboardEntry,
} from '../domain/types';

// Friends (the FRIENDS wave). The list, the pending requests and the leaderboard are queries; the
// add/accept/decline/remove/block actions are mutations that invalidate them together. Every friend
// query key starts with ['friends'], so invalidating that prefix refreshes the list, the requests
// and the leaderboard in one call; the profile is invalidated too because rarity numbers can move.

export function useFriends(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<Friend[]>({
    queryKey: ['friends'],
    queryFn: ({ signal }) => client.friends(signal),
    enabled,
  });
}

export function useFriendRequests(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<FriendRequests>({
    queryKey: ['friends', 'requests'],
    queryFn: ({ signal }) => client.friendRequests(signal),
    enabled,
  });
}

export function useLeaderboard(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<LeaderboardEntry[]>({
    queryKey: ['friends', 'leaderboard'],
    queryFn: ({ signal }) => client.leaderboard(signal),
    enabled,
  });
}

// A friend's grimoire — only fetched once a friend is selected to view it (403 when not friends).
export function useFriendGrimoire(friendId: string | null) {
  const client = useGrimoireClient();

  return useQuery<GrimoireEntry[]>({
    queryKey: ['friends', friendId ?? '', 'grimoire'],
    queryFn: ({ signal }) => client.friendGrimoire(friendId as string, signal),
    enabled: friendId !== null,
    retry: false,
  });
}

// The cross of the caller's grimoire with a friend's (403 when not friends).
export function useFriendCrossed(friendId: string | null) {
  const client = useGrimoireClient();

  return useQuery<CrossedGrimoires>({
    queryKey: ['friends', friendId ?? '', 'crossed'],
    queryFn: ({ signal }) => client.friendCrossed(friendId as string, signal),
    enabled: friendId !== null,
    retry: false,
  });
}

// A friend's Atlas point (both coords null when they have no taste vector yet).
export function useFriendAtlasPoint(friendId: string | null) {
  const client = useGrimoireClient();

  return useQuery<FriendAtlasPoint>({
    queryKey: ['friends', friendId ?? '', 'atlas-point'],
    queryFn: ({ signal }) => client.friendAtlasPoint(friendId as string, signal),
    enabled: friendId !== null,
    retry: false,
  });
}

// A taste duel with a friend — only fetched once the duel view is opened (403 when not friends).
export function useFriendDuel(friendId: string | null, enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<FriendDuel>({
    queryKey: ['friends', friendId ?? '', 'duel'],
    queryFn: ({ signal }) => client.friendDuel(friendId as string, signal),
    enabled: enabled && friendId !== null,
    retry: false,
  });
}

// Challenges a friend to a taste duel: it drops a notification on their side, so nothing here
// invalidates the caller's own views. 403 (not friends) surfaces as ApiError for the caller to read.
export function useChallengeDuel() {
  const client = useGrimoireClient();

  return useMutation<void, unknown, string>({
    mutationFn: (friendId) => client.challengeDuel(friendId),
  });
}

// Every friend mutation refreshes the whole friends surface (list + requests + leaderboard) and the
// profile, since accepting a friend or removing one changes what those views show.
function useFriendsInvalidation() {
  const queryClient = useQueryClient();

  return () => {
    void queryClient.invalidateQueries({ queryKey: ['friends'] });
    void queryClient.invalidateQueries({ queryKey: ['profile'] });
  };
}

// Adds a friend by handle (or accepts a matching incoming request). 404/400/409 surface as ApiError.
export function useRequestFriend() {
  const client = useGrimoireClient();
  const invalidate = useFriendsInvalidation();

  return useMutation<void, unknown, string>({
    mutationFn: (handle) => client.requestFriend(handle),
    onSuccess: invalidate,
  });
}

export function useAcceptFriend() {
  const client = useGrimoireClient();
  const invalidate = useFriendsInvalidation();

  return useMutation<void, unknown, string>({
    mutationFn: (friendshipId) => client.acceptFriend(friendshipId),
    onSuccess: invalidate,
  });
}

export function useDeclineFriend() {
  const client = useGrimoireClient();
  const invalidate = useFriendsInvalidation();

  return useMutation<void, unknown, string>({
    mutationFn: (friendshipId) => client.declineFriend(friendshipId),
    onSuccess: invalidate,
  });
}

export function useRemoveFriend() {
  const client = useGrimoireClient();
  const invalidate = useFriendsInvalidation();

  return useMutation<void, unknown, string>({
    mutationFn: (friendshipId) => client.removeFriend(friendshipId),
    onSuccess: invalidate,
  });
}

// Blocks a user by their user id (not the friendship id — a block outlives the friendship).
export function useBlockUser() {
  const client = useGrimoireClient();
  const invalidate = useFriendsInvalidation();

  return useMutation<void, unknown, string>({
    mutationFn: (userId) => client.blockUser(userId),
    onSuccess: invalidate,
  });
}

// Sends a friend a blind gift of a band (the NOTIFICATIONS wave). It lands in their inbox and stays
// blind until they open it, so nothing here invalidates the sender's own views. 403 (not friends)
// and 404 (artist missing) surface as ApiError for the caller to read.
export function useGiftToFriend() {
  const client = useGrimoireClient();

  return useMutation<void, unknown, { friendId: string; artistId: string }>({
    mutationFn: ({ friendId, artistId }) => client.giftToFriend(friendId, artistId),
  });
}
