import { useMutation, useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { NotifyResult, WeeklyRite } from '../domain/types';

// The current ISO week's seven blind bands (feature B17). Only fetched when signed in with a
// taste; the backend returns 409 otherwise (run cold start first). The GET is idempotent —
// it reuses the week's already-served rites and only mints the missing ones.
export function useWeekly(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<WeeklyRite>({
    queryKey: ['weekly'],
    queryFn: ({ signal }) => client.weekly(signal),
    enabled,
    retry: false,
  });
}

// Triggers a Web Push for the current Weekly Rite (feature B17 delivery, manual/test). Rejects
// with a 503 ApiError when Web Push is not configured (no VAPID key), which the UI reports plainly.
export function useNotifyWeekly() {
  const client = useGrimoireClient();

  return useMutation<NotifyResult, unknown, void>({
    mutationFn: () => client.notifyWeekly(),
  });
}
