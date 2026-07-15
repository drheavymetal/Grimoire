import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { Notification } from '../domain/types';

// The NOTIFICATIONS wave — a POLLED in-app inbox (not web push). The unread count feeds the sidebar
// badge and is refetched on an interval and on tab refocus; the list feeds the page. Every key
// starts with ['notifications'], so a mark-read/mark-all mutation invalidates the prefix and both
// the badge and the list refresh in one call.

// How many notifications one page of the list pulls (the page is a flat, newest-first slice).
const PAGE_SIZE = 30;

// The unread count for the sidebar badge. Polls every ~60s and on tab refocus, but only while the
// caller is signed in (an anonymous visitor has no inbox, so the query stays disabled).
export function useUnreadCount(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<number>({
    queryKey: ['notifications', 'unread-count'],
    queryFn: ({ signal }) => client.unreadCount(signal),
    enabled,
    refetchInterval: 60_000,
    refetchOnWindowFocus: true,
  });
}

// The notification list (newest first). Refetched on tab refocus so the page freshens when the
// listener comes back to the tab.
export function useNotifications(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<Notification[]>({
    queryKey: ['notifications', 'list'],
    queryFn: ({ signal }) => client.notifications(0, PAGE_SIZE, signal),
    enabled,
    refetchOnWindowFocus: true,
  });
}

// Any mark-read mutation moves both the list (a row turns read) and the badge (the count drops), so
// invalidating the ['notifications'] prefix refreshes them together.
function useNotificationsInvalidation() {
  const queryClient = useQueryClient();

  return () => {
    void queryClient.invalidateQueries({ queryKey: ['notifications'] });
  };
}

// Marks one notification as read.
export function useMarkRead() {
  const client = useGrimoireClient();
  const invalidate = useNotificationsInvalidation();

  return useMutation<void, unknown, string>({
    mutationFn: (id) => client.markRead(id),
    onSuccess: invalidate,
  });
}

// Marks every notification as read (clears the badge). Returns how many were marked.
export function useMarkAllRead() {
  const client = useGrimoireClient();
  const invalidate = useNotificationsInvalidation();

  return useMutation<number, unknown, void>({
    mutationFn: () => client.markAllRead(),
    onSuccess: invalidate,
  });
}
