import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { BandCard, Profile, RebuildResult } from '../domain/types';

// The signed-in listener's profile (2026-07-15): depth score, counts, deepest cut, and the shape
// of their grimoire. `enabled` gates it on being signed in.
export function useProfile(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<Profile>({
    queryKey: ['profile'],
    queryFn: ({ signal }) => client.getProfile(signal),
    enabled,
  });
}

// The listener's pinned taste anchors — the editable seed set (the hybrid-taste half).
export function useAnchors(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<BandCard[]>({
    queryKey: ['profile', 'anchors'],
    queryFn: ({ signal }) => client.getAnchors(signal),
    enabled,
  });
}

// After any anchor mutation the profile counts and the taste itself may move, so the profile,
// anchors and rite-taste queries are all invalidated together.
function useProfileInvalidation() {
  const queryClient = useQueryClient();

  return () => {
    void queryClient.invalidateQueries({ queryKey: ['profile'] });
    void queryClient.invalidateQueries({ queryKey: ['profile', 'anchors'] });
    void queryClient.invalidateQueries({ queryKey: ['rite', 'taste'] });
  };
}

// Pins a band as a taste anchor.
export function useAddAnchor() {
  const client = useGrimoireClient();
  const invalidate = useProfileInvalidation();

  return useMutation<void, unknown, string>({
    mutationFn: (artistId) => client.addAnchor(artistId),
    onSuccess: invalidate,
  });
}

// Unpins a taste anchor.
export function useRemoveAnchor() {
  const client = useGrimoireClient();
  const invalidate = useProfileInvalidation();

  return useMutation<void, unknown, string>({
    mutationFn: (artistId) => client.removeAnchor(artistId),
    onSuccess: invalidate,
  });
}

// Re-seeds the taste vector from the pinned anchors' mean (the "rebuild my taste" button). A 400
// (no usable anchor) surfaces as an ApiError the caller can read.
export function useRebuildTaste() {
  const client = useGrimoireClient();
  const invalidate = useProfileInvalidation();

  return useMutation<RebuildResult, unknown, void>({
    mutationFn: () => client.rebuildTaste(),
    onSuccess: invalidate,
  });
}
