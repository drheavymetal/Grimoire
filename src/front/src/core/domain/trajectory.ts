import type { TrajectoryPoint } from './types';

// Pure geometry for the taste-trajectory view (feature C16). No DOM, no platform coupling
// (invariant 6): it maps the Atlas-plane snapshot positions into a fixed SVG viewbox so the
// ui/ layer only draws primitives. Tested without a browser.

export interface TrajectoryLayout {
  // The projected points that have an Atlas position, in chronological order, mapped to the
  // [0, width] x [0, height] viewbox (y flipped so larger world-y is higher on screen).
  points: Array<{ cx: number; cy: number; point: TrajectoryPoint }>;
  width: number;
  height: number;
}

// Maps the projectable trajectory points into a padded viewbox. Points without an Atlas
// position (x/y null) are dropped — never placed at the origin, which would invent a location.
// A single point sits at the centre; degenerate spreads (all points coincide) also centre.
export function layoutTrajectory(
  all: readonly TrajectoryPoint[],
  width = 320,
  height = 200,
  pad = 16,
): TrajectoryLayout {
  const placed = all.filter(
    (p): p is TrajectoryPoint & { x: number; y: number } => p.x !== null && p.y !== null,
  );

  if (placed.length === 0) {
    return { points: [], width, height };
  }

  const xs = placed.map((p) => p.x);
  const ys = placed.map((p) => p.y);
  const minX = Math.min(...xs);
  const maxX = Math.max(...xs);
  const minY = Math.min(...ys);
  const maxY = Math.max(...ys);

  const spanX = maxX - minX;
  const spanY = maxY - minY;
  const innerW = width - 2 * pad;
  const innerH = height - 2 * pad;

  const mapped = placed.map((point) => {
    // When a span is zero (single point or a straight line on that axis), centre on that axis.
    const cx = spanX === 0 ? width / 2 : pad + ((point.x - minX) / spanX) * innerW;
    const cyWorld = spanY === 0 ? height / 2 : pad + ((point.y - minY) / spanY) * innerH;
    // Flip y so higher world-y is higher on screen (SVG y grows downward).
    const cy = spanY === 0 ? cyWorld : height - cyWorld;
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
