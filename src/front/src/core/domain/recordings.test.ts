import { describe, expect, it } from 'vitest';
import { formatAverageLength, formatTrackLength } from './recordings';

describe('formatTrackLength', () => {
  it('renders minutes and zero-padded seconds', () => {
    expect(formatTrackLength(0)).toBe('0:00');
    expect(formatTrackLength(1000)).toBe('0:01');
    expect(formatTrackLength(59000)).toBe('0:59');
    expect(formatTrackLength(60000)).toBe('1:00');
    // Darkthrone, "Cromlech" — a real length from the live base.
    expect(formatTrackLength(251280)).toBe('4:11');
  });

  it('rolls over into hours past 3600 seconds', () => {
    expect(formatTrackLength(3600000)).toBe('1:00:00');
    expect(formatTrackLength(3661000)).toBe('1:01:01');
  });

  it('renders a null length as an em dash, never 0:00', () => {
    expect(formatTrackLength(null)).toBe('—');
  });

  it('treats a negative length as missing', () => {
    expect(formatTrackLength(-1)).toBe('—');
  });
});

describe('formatAverageLength', () => {
  it('rounds a fractional millisecond average to the nearest second', () => {
    // 251280.4 ms → 251 s → 4:11.
    expect(formatAverageLength(251280.4)).toBe('4:11');
    // 7000.9 ms → 7 s.
    expect(formatAverageLength(7000.9)).toBe('0:07');
  });
});
