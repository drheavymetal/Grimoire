import { describe, expect, it } from 'vitest';
import { comfortToPercentileBand, riskFromComfort, RING_WIDTH_PCT } from './rite';

// Runs in a plain Node environment (no DOM): core stays portable (D12). These mirror the
// backend RingResolverTests, so the slider the user moves means the same thing on both ends.
describe('comfortToPercentileBand', () => {
  it('maps comfort 0 to the nearest band [0, width]', () => {
    const band = comfortToPercentileBand(0);
    expect(band.low).toBeCloseTo(0);
    expect(band.high).toBeCloseTo(RING_WIDTH_PCT);
  });

  it('maps comfort 1 to the farthest band [1 - width, 1]', () => {
    const band = comfortToPercentileBand(1);
    expect(band.low).toBeCloseTo(1 - RING_WIDTH_PCT);
    expect(band.high).toBeCloseTo(1);
  });

  it('keeps a strict sub-range: comfort is always nearer than abyss', () => {
    const comfort = comfortToPercentileBand(0.2);
    const abyss = comfortToPercentileBand(0.9);
    expect(comfort.low).toBeLessThan(abyss.low);
    expect(comfort.high).toBeLessThan(abyss.high);
  });

  it('preserves the band width at every position', () => {
    for (const c of [0, 0.25, 0.5, 0.75, 1]) {
      const band = comfortToPercentileBand(c);
      expect(band.high - band.low).toBeCloseTo(RING_WIDTH_PCT);
    }
  });

  it('clamps out-of-range comfort into [0, 1]', () => {
    expect(comfortToPercentileBand(-1)).toEqual(comfortToPercentileBand(0));
    expect(comfortToPercentileBand(2)).toEqual(comfortToPercentileBand(1));
  });

  it('rejects a degenerate band width', () => {
    expect(() => comfortToPercentileBand(0.5, 0)).toThrow(RangeError);
    expect(() => comfortToPercentileBand(0.5, 1)).toThrow(RangeError);
  });
});

describe('riskFromComfort', () => {
  it('is the midpoint of the band, rising with comfort', () => {
    expect(riskFromComfort(0)).toBeCloseTo(RING_WIDTH_PCT / 2);
    expect(riskFromComfort(1)).toBeCloseTo(1 - RING_WIDTH_PCT / 2);
    expect(riskFromComfort(0.2)).toBeLessThan(riskFromComfort(0.8));
  });
});
