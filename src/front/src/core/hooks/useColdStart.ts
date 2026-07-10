import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { TasteStatus } from '../domain/types';

// Seeds the taste from picked bands (D15). On success the taste query is invalidated so
// the UI leaves cold start and enters The Rite.
export function useSeed() {
  const client = useGrimoireClient();
  const queryClient = useQueryClient();

  return useMutation<TasteStatus, unknown, string[]>({
    mutationFn: (artistIds) => client.seed(artistIds),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['rite', 'taste'] });
    },
  });
}

// Cold start from Last.fm scrobbles (feature C1). BLOCKED without an API key: the endpoint
// answers 503, which the UI must present as "not available yet", not a broken error. The
// mutation surfaces the ApiError so the caller can read its status.
export function useImportLastFm() {
  const client = useGrimoireClient();
  const queryClient = useQueryClient();

  return useMutation<TasteStatus, unknown, string>({
    mutationFn: (username) => client.importLastFm(username),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['rite', 'taste'] });
    },
  });
}
