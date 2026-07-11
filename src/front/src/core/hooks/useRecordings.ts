import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { ArtistDuration, ArtistThemes, Track, VersionGraph } from '../domain/types';

// The recording-feature hooks (movement V, over the tracklist): B5 tracklist, C21 title mining,
// C10 the version graph, C7 the duration axis. Each wraps one endpoint through the injected client,
// so nothing here touches the network or the DOM directly (invariant 6, D12). Disabled queries fire
// nothing until they are wanted — the tracklist only loads when a release row is expanded.

// B5 — the tracklist of one release. Only fetched when the row is open, so a discography of forty
// releases does not fan out forty requests on mount.
export function useReleaseTracks(artistId: string, releaseId: string, enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<Track[]>({
    queryKey: ['recordings', 'tracks', artistId, releaseId],
    queryFn: ({ signal }) => client.releaseTracks(artistId, releaseId, signal),
    enabled: enabled && artistId.length > 0 && releaseId.length > 0,
  });
}

// C21 — the lyrical themes a band's titles evoke (an approximation).
export function useArtistThemes(id: string) {
  const client = useGrimoireClient();

  return useQuery<ArtistThemes>({
    queryKey: ['recordings', 'themes', id],
    queryFn: ({ signal }) => client.artistThemes(id, signal),
    enabled: id.length > 0,
  });
}

// C10 — the cross-artist covers touching a band's recordings.
export function useArtistVersions(id: string) {
  const client = useGrimoireClient();

  return useQuery<VersionGraph>({
    queryKey: ['recordings', 'versions', id],
    queryFn: ({ signal }) => client.artistVersions(id, signal),
    enabled: id.length > 0,
  });
}

// C7 — bands ranked by mean track length toward one pole (long = funeral doom, short = grindcore).
export function useDurationAxis(pole: 'long' | 'short', limit: number) {
  const client = useGrimoireClient();

  return useQuery<ArtistDuration[]>({
    queryKey: ['recordings', 'duration-axis', pole, limit],
    queryFn: ({ signal }) => client.durationAxis(pole, limit, signal),
  });
}
