import { describe, expect, it } from 'vitest';
import { layoutTrajectory, polylinePoints } from './trajectory';
import type { TrajectoryPoint } from './types';

// The layout plots depth over time now, so a point is defined by its depth score; x/y (the old Atlas
// position) are irrelevant to the geometry and kept null.
function point(depthScore: number): TrajectoryPoint {
  return { createdAt: '2026-07-11T00:00:00Z', depthScore, drift: 0, x: null, y: null };
}

describe('layoutTrajectory', () => {
  it('spreads snapshots over time on x and maps depth on y, flipped', () => {
    const layout = layoutTrajectory([point(0), point(10)], 100, 100, 10);

    expect(layout.points).toHaveLength(2);
    // First snapshot: left pad, min depth → flipped to the bottom.
    expect(layout.points[0].cx).toBeCloseTo(10, 5);
    expect(layout.points[0].cy).toBeCloseTo(90, 5);
    // Last snapshot: right inner edge, max depth → flipped to the top.
    expect(layout.points[1].cx).toBeCloseTo(90, 5);
    expect(layout.points[1].cy).toBeCloseTo(10, 5);
  });

  it('draws a level line at mid-height when every depth is equal (no deepening yet)', () => {
    const layout = layoutTrajectory([point(0), point(0), point(0)], 100, 200, 10);

    expect(layout.points).toHaveLength(3);
    for (const p of layout.points) {
      expect(p.cy).toBeCloseTo(100, 5);
    }
  });

  it('places a single snapshot at the left rather than dividing by a zero time span', () => {
    const layout = layoutTrajectory([point(5)], 100, 200, 10);

    expect(layout.points).toHaveLength(1);
    expect(layout.points[0].cx).toBeCloseTo(10, 5);
  });

  it('returns no points for an empty history', () => {
    expect(layoutTrajectory([]).points).toHaveLength(0);
  });

  it('serialises a polyline points string in order', () => {
    const layout = layoutTrajectory([point(0), point(10)], 100, 100, 10);

    expect(polylinePoints(layout)).toBe('10,90 90,10');
  });
});
