// Deep search links to the major streaming services, built from a plain query (a band name, or a
// "band album" pair). Zero cost and no API keys: each service takes a search URL, so we never store
// or fetch anything — the link just opens the service's own search. Grimoire does not play music
// (invariant 4); this only points outward. Pure string building, no DOM (invariant 6).

export type StreamingService = 'spotify' | 'apple' | 'tidal' | 'youtube';

export interface StreamingLink {
  service: StreamingService;
  label: string;
  url: string;
}

export function streamingLinks(query: string): StreamingLink[] {
  const q = encodeURIComponent(query.trim());

  return [
    { service: 'spotify', label: 'Spotify', url: `https://open.spotify.com/search/${q}` },
    { service: 'apple', label: 'Apple Music', url: `https://music.apple.com/search?term=${q}` },
    { service: 'tidal', label: 'Tidal', url: `https://tidal.com/search?q=${q}` },
    { service: 'youtube', label: 'YouTube Music', url: `https://music.youtube.com/search?q=${q}` },
  ];
}
