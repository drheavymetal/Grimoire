import { describe, expect, it } from 'vitest';
import { streamingLinks } from './streaming';

describe('streamingLinks', () => {
  it('builds a search deep link for each of the four services', () => {
    const links = streamingLinks('Darkthrone');
    const services = links.map((l) => l.service);

    expect(services).toEqual(['spotify', 'apple', 'tidal', 'youtube']);
    expect(links.find((l) => l.service === 'spotify')!.url).toBe(
      'https://open.spotify.com/search/Darkthrone',
    );
    expect(links.find((l) => l.service === 'youtube')!.url).toBe(
      'https://music.youtube.com/search?q=Darkthrone',
    );
  });

  it('url-encodes spaces in queries', () => {
    const links = streamingLinks('Cult of Fire');

    for (const link of links) {
      expect(link.url).toContain('Cult%20of%20Fire');
    }
  });

  it('trims surrounding whitespace before encoding', () => {
    expect(streamingLinks('  Emperor  ')[0].url).toBe('https://open.spotify.com/search/Emperor');
  });
});
