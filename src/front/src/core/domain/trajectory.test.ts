import { describe, expect, it } from 'vitest';
import { layoutTrajectory, polylinePoints } from './trajectory';
import type { TrajectoryPoint } from './types';

function point(x: number | null, y: number | null): TrajectoryPoint {
  return { createdAt: '2026-07-11T00:00:00Z', depthScore: 0, drift: 0, x, y };
}

describe('layoutTrajectory', () => {
  it('maps points into the padded viewbox with y flipped', () => {
    const layout = layoutTrajectory([point(0, 0), point(1, 1)], 100, 100, 10);

    expect(layout.points).toHaveLength(2);
    // (0,0) is min on both axes: x at left pad, y flipped to the bottom.
    expect(layout.points[0].cx).toBeCloseTo(10, 5);
    expect(layout.points[0].cy).toBeCloseTo(90, 5);
    // (1,1) is max on both axes: x at right inner edge, y flipped to the top.
    expect(layout.points[1].cx).toBeCloseTo(90, 5);
    expect(layout.points[1].cy).toBeCloseTo(10, 5);
  });

  it('drops unprojectable points instead of placing them at the origin', () => {
    const layout = layoutTrajectory([point(0, 0), point(null, null), point(1, 1)]);

    // The null point is gone — never invented at (0,0).
    expect(layout.points).toHaveLength(2);
  });

  it('centres a single point rather than dividing by a zero span', () => {
    const layout = layoutTrajectory([point(5, 5)], 100, 200, 10);

    expect(layout.points).toHaveLength(1);
    expect(layout.points[0].cx).toBeCloseTo(50, 5);
    expect(layout.points[0].cy).toBeCloseTo(100, 5);
  });

  it('returns no points when none are projectable', () => {
    expect(layoutTrajectory([point(null, null)]).points).toHaveLength(0);
  });

  it('serialises a polyline points string in order', () => {
    const layout = layoutTrajectory([point(0, 0), point(1, 1)], 100, 100, 10);

    expect(polylinePoints(layout)).toBe('10,90 90,10');
  });
});
