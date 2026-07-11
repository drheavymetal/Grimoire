import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { Atlas } from '../domain/types';

// The catalogue as a 2D star field (C18/B22). The query key carries whether a taste is expected so
// the "you are here" marker refreshes when the user signs in or seeds their taste, rather than
// serving a cached anonymous field.
export function useAtlas(hasTaste: boolean) {
  const client = useGrimoireClient();

  return useQuery<Atlas>({
    queryKey: ['atlas', hasTaste],
    queryFn: ({ signal }) => client.atlas(signal),
  });
}
