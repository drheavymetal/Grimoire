import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { LogoutAllResult, Session } from '../domain/types';

// The caller's active sessions (D28, the FRIENDS wave surfaces the refresh tokens). `enabled` gates
// it on being signed in.
export function useSessions(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<Session[]>({
    queryKey: ['auth', 'sessions'],
    queryFn: ({ signal }) => client.sessions(signal),
    enabled,
  });
}

// "Log out everywhere": revokes every session of the caller and reports how many were killed. The
// current session is revoked too, so the caller signs out locally right after (see the profile page).
export function useLogoutAll() {
  const client = useGrimoireClient();
  const queryClient = useQueryClient();

  return useMutation<LogoutAllResult, unknown, void>({
    mutationFn: () => client.logoutAll(),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['auth', 'sessions'] });
    },
  });
}
