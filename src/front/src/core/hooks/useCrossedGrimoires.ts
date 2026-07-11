import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { CrossedGrimoires, GrimoireCode } from '../domain/types';

// C23 — crossed grimoires. The caller's own code (to share), and the cross with a friend's code.

export function useGrimoireCode(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<GrimoireCode>({
    queryKey: ['grimoire-code'],
    queryFn: ({ signal }) => client.grimoireCode(signal),
    enabled,
  });
}

export function useCrossGrimoires(other: string) {
  const client = useGrimoireClient();

  return useQuery<CrossedGrimoires>({
    queryKey: ['crossed-grimoires', other],
    queryFn: ({ signal }) => client.crossGrimoires(other, signal),
    enabled: other.length > 0,
    retry: false,
  });
}
