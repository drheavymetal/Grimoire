import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { Scene } from '../domain/types';

// B20/C11 — the scenes of the catalogue (city + decade + tag). Portable: the hook only wraps the
// injected client, so it runs unchanged under React Native (D12).
export function useScenes(minSize = 3) {
  const client = useGrimoireClient();

  return useQuery<Scene[]>({
    queryKey: ['scenes', minSize],
    queryFn: ({ signal }) => client.scenes(minSize, signal),
    staleTime: 60_000,
  });
}
