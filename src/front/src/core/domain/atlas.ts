// The Atlas coordinate math (C18/B22). Pure and portable — no canvas, no DOM — so the map's
// projection and scale can be tested without rendering a single pixel (the canvas itself is the
// one view that lives only in ui/, the explicit exception to invariant 6 — D18/D24). The star
// positions arrive already projected to 2D from the backend; this module only fits that point
// cloud into a viewport and answers "which stars sit nearest the taste".

import { fitToViewport, transformPoint, type Bounds, type ViewFit } from './graph';
import type { AtlasStar, AtlasTaste } from './types';

export type { Bounds, ViewFit } from './graph';

// A world-space point the Atlas has to place: a star, or the taste marker.
export interface AtlasPoint {
  x: number;
  y: number;
}

// The bounding box of a set of points. An empty set yields a zero box at the origin (a designed
// empty Atlas), and a single point a zero-size box at itself — never NaN.
export function atlasBounds(points: readonly AtlasPoint[]): Bounds {
  if (points.length === 0) {
    return { minX: 0, minY: 0, maxX: 0, maxY: 0 };
  }

  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;

  for (const p of points) {
    minX = Math.min(minX, p.x);
    minY = Math.min(minY, p.y);
    maxX = Math.max(maxX, p.x);
    maxY = Math.max(maxY, p.y);
  }

  return { minX, minY, maxX, maxY };
}

// The transform that fits the whole star field (and the taste marker, when present) into a
// viewport with uniform scale and centring. Reuses the graph auto-fit (SPEC §9) so the Atlas and
// the lineage graphs place points by the same tested rule. The taste is included in the bounds so
// "you are here" is never clipped off-screen.
export function fitAtlas(
  stars: readonly AtlasStar[],
  taste: AtlasTaste | null,
  viewport: { width: number; height: number },
  padding: number,
): ViewFit {
  const points: AtlasPoint[] = taste === null ? [...stars] : [...stars, taste];
  return fitToViewport(atlasBounds(points), viewport, padding);
}

// Screen position of a world point under a fit (plus the user's zoom about the viewport centre and
// pan). Kept identical in shape to the graph painter so the two views share one mental model.
export function atlasScreenOf(
  point: AtlasPoint,
  fit: ViewFit,
  view: { zoom: number; panX: number; panY: number; centreX: number; centreY: number },
): AtlasPoint {
  const base = transformPoint(point.x, point.y, fit);
  return {
    x: (base.x - view.centreX) * view.zoom + view.centreX + view.panX,
    y: (base.y - view.centreY) * view.zoom + view.centreY + view.panY,
  };
}

// The squared world-space distance between two points (no sqrt — ordering only).
function distanceSquared(a: AtlasPoint, b: AtlasPoint): number {
  const dx = a.x - b.x;
  const dy = a.y - b.y;
  return dx * dx + dy * dy;
}

// The ids of the `count` stars nearest the taste in the plane — the ones the Atlas paints alive and
// sulphur (DESIGN §5). Pure, so the "which stars are near you" rule is tested without a canvas.
// With no taste (anonymous, or no vector yet) nothing is alive: an empty set, a designed state.
export function starsNearTaste(
  stars: readonly AtlasStar[],
  taste: AtlasTaste | null,
  count: number,
): Set<string> {
  if (taste === null || count <= 0) {
    return new Set<string>();
  }

  return new Set(
    [...stars]
      .sort((a, b) => distanceSquared(a, taste) - distanceSquared(b, taste))
      .slice(0, count)
      .map((star) => star.id),
  );
}
