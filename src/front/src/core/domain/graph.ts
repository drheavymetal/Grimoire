import {
  forceCenter,
  forceCollide,
  forceLink,
  forceManyBody,
  forceSimulation,
  type SimulationLinkDatum,
  type SimulationNodeDatum,
} from 'd3-force';
import type { Graph, GraphNode } from './types';

// The shared graph engine (movement IV, DECISIONS D18 / SPEC §9). It computes a force-directed
// layout with d3-force run HEADLESS — d3-force is pure JS, no DOM, so it lives here in core/ and
// survives the React Native port (invariant 6). The painting is SVG primitives in ui/GraphCanvas,
// which transforms these positions in JS (never scaling a <g>). We do NOT use react-force-graph:
// it is canvas-bound and ties repaint to the simulation loop (D18).
//
// Everything here is deterministic: initial positions are seeded on a circle and the RNG is a
// fixed generator, so the same graph always lays out the same way and the layout can be tested
// without a browser.

export interface PositionedNode extends GraphNode {
  x: number;
  y: number;
}

export interface Bounds {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export interface GraphLayout {
  nodes: PositionedNode[];
  bounds: Bounds;
}

// The transform that fits graph coordinates into a viewport: screen = coord * scale + translate.
export interface ViewFit {
  scale: number;
  translateX: number;
  translateY: number;
}

interface SimNode extends SimulationNodeDatum {
  id: string;
}

const DEFAULT_ITERATIONS = 300;
const CHARGE = -220;
const LINK_DISTANCE = 60;
const COLLIDE_RADIUS = 22;
const INITIAL_RING = 120;

// A small seeded PRNG (mulberry32) so the layout — including d3's jiggle for coincident nodes —
// is reproducible run to run and in tests.
function seededRandom(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a |= 0;
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/**
 * Runs a force-directed layout for a graph, headless. Returns each node with a settled x/y and the
 * bounding box of the result. Deterministic: nodes start on a circle by index and the simulation
 * uses a fixed RNG.
 */
export function layoutGraph(graph: Graph, iterations: number = DEFAULT_ITERATIONS): GraphLayout {
  const count = graph.nodes.length;

  if (count === 0) {
    return { nodes: [], bounds: { minX: 0, minY: 0, maxX: 0, maxY: 0 } };
  }

  // Fresh simulation nodes seeded on a ring — never mutate the caller's data.
  const simNodes: SimNode[] = graph.nodes.map((node, i) => {
    const angle = (2 * Math.PI * i) / count;
    return {
      id: node.id,
      x: Math.cos(angle) * INITIAL_RING,
      y: Math.sin(angle) * INITIAL_RING,
    };
  });

  const simLinks: SimulationLinkDatum<SimNode>[] = graph.edges.map((edge) => ({
    source: edge.source,
    target: edge.target,
  }));

  const simulation = forceSimulation(simNodes)
    .randomSource(seededRandom(0x9e3779b9))
    .force('link', forceLink<SimNode, SimulationLinkDatum<SimNode>>(simLinks).id((d) => d.id).distance(LINK_DISTANCE))
    .force('charge', forceManyBody().strength(CHARGE))
    .force('center', forceCenter(0, 0))
    .force('collide', forceCollide(COLLIDE_RADIUS))
    .stop();

  // Advance the simulation to a settled state without ever rendering a frame.
  simulation.tick(iterations);

  const nodes: PositionedNode[] = graph.nodes.map((node, i) => ({
    ...node,
    x: simNodes[i].x ?? 0,
    y: simNodes[i].y ?? 0,
  }));

  return { nodes, bounds: computeBounds(nodes) };
}

/** The bounding box of a set of positioned nodes. A single node yields a zero-size box at itself. */
export function computeBounds(nodes: readonly PositionedNode[]): Bounds {
  if (nodes.length === 0) {
    return { minX: 0, minY: 0, maxX: 0, maxY: 0 };
  }

  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;

  for (const node of nodes) {
    minX = Math.min(minX, node.x);
    minY = Math.min(minY, node.y);
    maxX = Math.max(maxX, node.x);
    maxY = Math.max(maxY, node.y);
  }

  return { minX, minY, maxX, maxY };
}

/**
 * The transform that fits a bounding box into a viewport with uniform scale and centring — the
 * auto-fit (SPEC §9): the graph is scaled to fit, then its positions are transformed in JS. A
 * degenerate (zero-size) box is placed at the centre at scale 1, never divided by zero.
 */
export function fitToViewport(
  bounds: Bounds,
  viewport: { width: number; height: number },
  padding: number,
): ViewFit {
  const contentW = bounds.maxX - bounds.minX;
  const contentH = bounds.maxY - bounds.minY;
  const availW = Math.max(1, viewport.width - 2 * padding);
  const availH = Math.max(1, viewport.height - 2 * padding);

  // A zero extent (single node, or a straight line in one axis) must not scale that axis to
  // infinity: fall back to 1 on any dimension with no extent.
  const scaleX = contentW > 0 ? availW / contentW : 1;
  const scaleY = contentH > 0 ? availH / contentH : 1;
  const scale = Math.min(scaleX, scaleY);

  const drawnW = contentW * scale;
  const drawnH = contentH * scale;
  const translateX = padding + (availW - drawnW) / 2 - bounds.minX * scale;
  const translateY = padding + (availH - drawnH) / 2 - bounds.minY * scale;

  return { scale, translateX, translateY };
}

/** Applies a ViewFit to a graph-space point, giving a screen-space point. */
export function transformPoint(x: number, y: number, fit: ViewFit): { x: number; y: number } {
  return { x: x * fit.scale + fit.translateX, y: y * fit.scale + fit.translateY };
}

/**
 * Whether a node's label should be drawn (the GraphCanvas rule, SPEC §9): always when it is focused
 * or matches a search, otherwise only once the user has zoomed in past the threshold — below it the
 * labels would overlap into noise.
 */
export const LABEL_ZOOM_THRESHOLD = 1.6;

export function shouldShowLabel(opts: { focused: boolean; matched: boolean; zoom: number }): boolean {
  return opts.focused || opts.matched || opts.zoom >= LABEL_ZOOM_THRESHOLD;
}
