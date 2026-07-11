import { describe, expect, it } from 'vitest';
import { resolveArtistView } from './artistView';

// The band-vs-composer decision (movement VII, D11). These bite: the works signal must win over
// kind, or a composer (a Person) would render the member page instead of the composer body.
describe('resolveArtistView', () => {
  it('routes an artist with works to the composer body, even though a composer is a Person', () => {
    expect(resolveArtistView({ hasWorks: true, kind: 'Person' })).toBe('composer');
  });

  it('routes a Group with no works to the band ficha (the Gantt)', () => {
    expect(resolveArtistView({ hasWorks: false, kind: 'Group' })).toBe('band');
  });

  it('routes a Person with no works to the member page (B10)', () => {
    expect(resolveArtistView({ hasWorks: false, kind: 'Person' })).toBe('member');
  });

  it('lets works win over a Group too (an orchestra that also composed stays a composer)', () => {
    // The rule is "works first"; flipping the order (kind before works) would break this.
    expect(resolveArtistView({ hasWorks: true, kind: 'Group' })).toBe('composer');
    expect(resolveArtistView({ hasWorks: true, kind: 'Orchestra' })).toBe('composer');
  });
});
