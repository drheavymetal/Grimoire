import { describe, expect, it } from 'vitest';
import { REVEAL_DURATION_MS, shouldAnimateReveal } from './reveal';

// The reveal gate is a pure function of the reduced-motion preference, so both branches are
// tested without a browser (D12, DESIGN 5/7).
describe('shouldAnimateReveal', () => {
  it('animates when the user has no reduced-motion preference', () => {
    expect(shouldAnimateReveal(false)).toBe(true);
  });

  it('does not animate when the user prefers reduced motion (name shown resolved at once)', () => {
    expect(shouldAnimateReveal(true)).toBe(false);
  });
});

describe('REVEAL_DURATION_MS', () => {
  it('is the 600 ms photographic develop from DESIGN 3.1', () => {
    expect(REVEAL_DURATION_MS).toBe(600);
  });
});
