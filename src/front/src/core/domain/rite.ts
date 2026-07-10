// Pure, portable slider maths for The Rite (features B13/B14, DECISIONS D4/D26).
// No DOM, no platform coupling — a plain function the UI and its tests both call.
//
// The Comfort <-> Abyss slider is a `comfort` value in [0, 1] that the server turns
// into a ring of the taste-to-artist distance distribution. This module mirrors the
// backend RingResolver exactly so the UI can SHOW the honest percentile band the
// engine will search — the number the user is moving, not a decoration.

// Width of the percentile band the slider slides (DECISIONS D26 / backend
// RiteEngineOptions.RingWidthPct and RingResolver.DefaultWidthPct).
export const RING_WIDTH_PCT = 0.2;

export interface PercentileBand {
  low: number;
  high: number;
}

// Maps the Comfort <-> Abyss slider to a percentile band, identical to the backend
// `RingResolver.Percentiles`: the band of width `widthPct` slides from the low end of
// the distribution (comfort 0, nearest neighbours) to the high end (comfort 1, the
// farthest bands still inside your tolerance).
//
//   comfort 0 -> [0.0, widthPct]
//   comfort 1 -> [1 - widthPct, 1.0]
//
// so the slider always selects a genuine ring, never the whole corpus (D26: an
// absolute cosine radius would select ~98% of the shell at any position).
export function comfortToPercentileBand(
  comfort: number,
  widthPct: number = RING_WIDTH_PCT,
): PercentileBand {
  if (widthPct <= 0 || widthPct >= 1) {
    throw new RangeError('Band width must be in (0, 1).');
  }

  const c = clamp01(comfort);
  const low = c * (1 - widthPct);
  const high = low + widthPct;

  return { low, high };
}

// The risk the server reports for a comfort value: the midpoint of the band
// (backend `riskPercentile = (loPct + hiPct) / 2`). Comfort 0 -> 0.1, comfort 1 -> 0.9.
export function riskFromComfort(comfort: number, widthPct: number = RING_WIDTH_PCT): number {
  const { low, high } = comfortToPercentileBand(comfort, widthPct);
  return (low + high) / 2;
}

function clamp01(value: number): number {
  if (Number.isNaN(value)) {
    return 0;
  }

  return Math.min(1, Math.max(0, value));
}
