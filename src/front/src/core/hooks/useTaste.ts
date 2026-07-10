import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { TasteStatus } from '../domain/types';

// Whether the signed-in user has a taste vector yet, so the UI knows whether to run the
// cold start (D15) or go straight to serving rites. `enabled` gates it on being signed in.
export function useTaste(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<TasteStatus>({
    queryKey: ['rite', 'taste'],
    queryFn: ({ signal }) => client.getTaste(signal),
    enabled,
  });
}
