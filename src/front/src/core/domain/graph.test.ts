import { describe, expect, it } from 'vitest';
import {
  computeBounds,
  fitToViewport,
  layoutGraph,
  LABEL_ZOOM_THRESHOLD,
  shouldShowLabel,
  transformPoint,
  type Bounds,
} from './graph';
import type { Graph } from './types';

// The shared graph engine (D18). These bite: the auto-fit math is what makes a graph of any size
// land inside its viewport, and the label rule is what keeps the canvas readable.

describe('fitToViewport (auto-fit)', () => {
  const square: Bounds = { minX: 0, minY: 0, maxX: 10, maxY: 10 };

  it('scales content to fill the padded viewport', () => {
    // 120px viewport, 10px padding → 100px available for a 10-unit box → scale 10.
    const fit = fitToViewport(square, { width: 120, height: 120 }, 10);
    expect(fit.scale).toBeCloseTo(10, 6);
  });

  it('doubling the viewport doubles the scale', () => {
    const small = fitToViewport(square, { width: 120, height: 120 }, 10);
    // Available scales linearly: (240-20)/10 = 22 vs (120-20)/10 = 10.
    const big = fitToViewport(square, { width: 240, height: 240 }, 10);
    expect(big.scale).toBeGreaterThan(small.scale);
    expect(big.scale).toBeCloseTo(22, 6);
  });

  it('keeps every corner inside the viewport', () => {
    const fit = fitToViewport(square, { width: 200, height: 140 }, 12);
    for (const [x, y] of [
      [square.minX, square.minY],
      [square.maxX, square.maxY],
    ] as const) {
      const p = transformPoint(x, y, fit);
      expect(p.x).toBeGreaterThanOrEqual(12 - 1e-6);
      expect(p.x).toBeLessThanOrEqual(200 - 12 + 1e-6);
      expect(p.y).toBeGreaterThanOrEqual(12 - 1e-6);
      expect(p.y).toBeLessThanOrEqual(140 - 12 + 1e-6);
    }
  });

  it('does not divide by zero for a single point (zero-size box)', () => {
    const point: Bounds = { minX: 5, minY: 5, maxX: 5, maxY: 5 };
    const fit = fitToViewport(point, { width: 100, height: 100 }, 10);
    expect(Number.isFinite(fit.scale)).toBe(true);
    expect(fit.scale).toBe(1);
    // The point lands at the viewport centre.
    const p = transformPoint(5, 5, fit);
    expect(p.x).toBeCloseTo(50, 6);
    expect(p.y).toBeCloseTo(50, 6);
  });
});

describe('shouldShowLabel', () => {
  it('shows when focused, regardless of zoom', () => {
    expect(shouldShowLabel({ focused: true, matched: false, zoom: 0.5 })).toBe(true);
  });

  it('shows when it matches a search, regardless of zoom', () => {
    expect(shouldShowLabel({ focused: false, matched: true, zoom: 0.5 })).toBe(true);
  });

  it('hides when zoomed out and neither focused nor matched', () => {
    expect(shouldShowLabel({ focused: false, matched: false, zoom: LABEL_ZOOM_THRESHOLD - 0.01 })).toBe(false);
  });

  it('shows once zoomed past the threshold', () => {
    expect(shouldShowLabel({ focused: false, matched: false, zoom: LABEL_ZOOM_THRESHOLD })).toBe(true);
  });
});

describe('computeBounds', () => {
  it('is the min/max envelope of the nodes', () => {
    const bounds = computeBounds([
      { id: 'a', name: 'A', kind: 'Group', rank: null, role: 'node', x: -3, y: 4 },
      { id: 'b', name: 'B', kind: 'Group', rank: null, role: 'node', x: 7, y: -2 },
    ]);
    expect(bounds).toEqual({ minX: -3, minY: -2, maxX: 7, maxY: 4 });
  });
});

describe('layoutGraph', () => {
  const graph: Graph = {
    nodes: [
      { id: 'a', name: 'A', kind: 'Group', rank: null, role: 'ego' },
      { id: 'b', name: 'B', kind: 'Person', rank: null, role: 'node' },
      { id: 'c', name: 'C', kind: 'Group', rank: null, role: 'node' },
    ],
    edges: [
      { source: 'a', target: 'b', kind: 'member', label: null },
      { source: 'b', target: 'c', kind: 'member', label: null },
    ],
  };

  it('places every node at a finite position', () => {
    const layout = layoutGraph(graph);
    expect(layout.nodes).toHaveLength(3);
    for (const node of layout.nodes) {
      expect(Number.isFinite(node.x)).toBe(true);
      expect(Number.isFinite(node.y)).toBe(true);
    }
  });

  it('is deterministic run to run (seeded)', () => {
    const a = layoutGraph(graph);
    const b = layoutGraph(graph);
    expect(a.nodes.map((n) => [n.x, n.y])).toEqual(b.nodes.map((n) => [n.x, n.y]));
  });

  it('separates connected nodes rather than stacking them', () => {
    const layout = layoutGraph(graph);
    const [a, b] = layout.nodes;
    const dist = Math.hypot(a.x - b.x, a.y - b.y);
    expect(dist).toBeGreaterThan(1);
  });

  it('does not mutate the input graph', () => {
    const before = JSON.stringify(graph);
    layoutGraph(graph);
    expect(JSON.stringify(graph)).toBe(before);
  });

  it('returns an empty layout for an empty graph', () => {
    const layout = layoutGraph({ nodes: [], edges: [] });
    expect(layout.nodes).toHaveLength(0);
    expect(layout.bounds).toEqual({ minX: 0, minY: 0, maxX: 0, maxY: 0 });
  });
});
