import { describe, expect, it } from 'vitest';
import { averageColor, rgbToHex } from './palette';

describe('averageColor', () => {
  it('averages the opaque pixels and skips transparent ones', () => {
    // Two opaque reds and one fully transparent pixel → the average is pure red, undiluted.
    const pixels = new Uint8ClampedArray([
      255, 0, 0, 255,
      255, 0, 0, 255,
      0, 0, 0, 0,
    ]);
    expect(averageColor(pixels)).toEqual({ r: 255, g: 0, b: 0 });
  });

  it('averages distinct opaque colours channel by channel', () => {
    // Black + white → mid grey.
    const pixels = new Uint8ClampedArray([
      0, 0, 0, 255,
      255, 255, 255, 255,
    ]);
    expect(averageColor(pixels)).toEqual({ r: 128, g: 128, b: 128 });
  });

  it('returns null when nothing is opaque', () => {
    const pixels = new Uint8ClampedArray([10, 20, 30, 0, 40, 50, 60, 4]);
    expect(averageColor(pixels)).toBeNull();
  });

  it('returns null for an empty buffer', () => {
    expect(averageColor(new Uint8ClampedArray([]))).toBeNull();
  });
});

describe('rgbToHex', () => {
  it('formats each channel as two lowercase hex digits', () => {
    expect(rgbToHex({ r: 255, g: 0, b: 128 })).toBe('#ff0080');
    expect(rgbToHex({ r: 0, g: 0, b: 0 })).toBe('#000000');
  });

  it('clamps out-of-range channels to a byte', () => {
    expect(rgbToHex({ r: 300, g: -5, b: 128 })).toBe('#ff0080');
  });
});
