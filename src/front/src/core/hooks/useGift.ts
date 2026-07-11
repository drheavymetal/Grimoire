import { useMutation, useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { ArtistDetail, Gift, GiftBlind } from '../domain/types';

// C22 — gift a discovery. Minting a gift and revealing one are actions (mutations); peeking at a
// received gift (the blind note + audio URL) is a query.

export function useCreateGift() {
  const client = useGrimoireClient();

  return useMutation<Gift, Error, { artistId: string; note: string | null }>({
    mutationFn: ({ artistId, note }) => client.createGift(artistId, note),
  });
}

export function usePeekGift(token: string) {
  const client = useGrimoireClient();

  return useQuery<GiftBlind>({
    queryKey: ['gift', token],
    queryFn: ({ signal }) => client.peekGift(token, signal),
    enabled: token.length > 0,
    retry: false,
  });
}

export function useRevealGift() {
  const client = useGrimoireClient();

  return useMutation<ArtistDetail, Error, string>({
    mutationFn: (token) => client.revealGift(token),
  });
}
