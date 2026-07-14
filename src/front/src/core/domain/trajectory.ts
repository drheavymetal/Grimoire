import type { TrajectoryPoint } from './types';

// Pure geometry for the taste-trajectory view (feature C16). No DOM, no platform coupling
// (invariant 6): it maps the snapshots into a fixed SVG viewbox so the ui/ layer only draws
// primitives. Tested without a browser.
//
// It plots DEPTH SCORE over time — the shape of the journey deepening — not the Atlas-plane path.
// The Atlas position (x/y) needs the live PCA projection, which is heavy and frequently unavailable,
// so a trajectory that depended on it just rendered nothing (the bug Pedro hit: "no veo nada"). Depth
// score and time are always present, so this always draws.

export interface TrajectoryLayout {
  // One point per snapshot in chronological order, mapped to the [0, width] x [0, height] viewbox
  // (y flipped so a higher depth score sits higher on screen).
  points: Array<{ cx: number; cy: number; point: TrajectoryPoint }>;
  width: number;
  height: number;
}

// Maps the snapshots into a padded viewbox: x is the snapshot's position in time (evenly spaced),
// y is its depth score. A flat run (every depth equal, e.g. all zero) draws a level line at mid
// height rather than collapsing — still a visible, honest "no deepening yet".
export function layoutTrajectory(
  all: readonly TrajectoryPoint[],
  width = 320,
  height = 200,
  pad = 16,
): TrajectoryLayout {
  if (all.length === 0) {
    return { points: [], width, height };
  }

  const depths = all.map((p) => p.depthScore);
  const minD = Math.min(...depths);
  const maxD = Math.max(...depths);
  const spanD = maxD - minD;

  const innerW = width - 2 * pad;
  const innerH = height - 2 * pad;
  const lastIndex = all.length - 1;

  const mapped = all.map((point, i) => {
    // One point: sit it at the left. Otherwise spread evenly across the inner width by time order.
    const cx = lastIndex === 0 ? pad : pad + (i / lastIndex) * innerW;
    // Flat depth (span 0): centre vertically. Otherwise map depth to height, flipped (SVG y grows down).
    const cyNorm = spanD === 0 ? 0.5 : (point.depthScore - minD) / spanD;
    const cy = height - (pad + cyNorm * innerH);
    return { cx, cy, point };
  });

  return { points: mapped, width, height };
}

// The SVG polyline "points" attribute for the mapped path, e.g. "16,20 30,44 ...".
export function polylinePoints(layout: TrajectoryLayout): string {
  return layout.points.map((p) => `${round(p.cx)},${round(p.cy)}`).join(' ');
}

function round(n: number): number {
  return Math.round(n * 100) / 100;
}
