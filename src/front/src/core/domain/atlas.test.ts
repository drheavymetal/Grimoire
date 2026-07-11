import { describe, expect, it } from 'vitest';
import { atlasBounds, atlasScreenOf, fitAtlas, starsNearTaste } from './atlas';
import type { AtlasStar, AtlasTaste } from './types';

function star(id: string, x: number, y: number): AtlasStar {
  return { id, name: id, kind: 'Group', rank: null, x, y };
}

const noZoom = { zoom: 1, panX: 0, panY: 0, centreX: 320, centreY: 220 };

describe('atlasBounds', () => {
  it('is a zero box at the origin for an empty field (designed empty Atlas)', () => {
    expect(atlasBounds([])).toEqual({ minX: 0, minY: 0, maxX: 0, maxY: 0 });
  });

  it('spans the extremes of the points', () => {
    const b = atlasBounds([star('a', -4, 1), star('b', 8, -5), star('c', 0, 7)]);
    expect(b).toEqual({ minX: -4, minY: -5, maxX: 8, maxY: 7 });
  });
});

describe('fitAtlas + atlasScreenOf', () => {
  const viewport = { width: 640, height: 440 };
  const padding = 30;
  const stars = [star('a', -4, -5), star('b', 8, 8), star('c', 0, 0)];

  it('places every star inside the padded viewport', () => {
    const fit = fitAtlas(stars, null, viewport, padding);
    for (const s of stars) {
      const p = atlasScreenOf(s, fit, noZoom);
      expect(p.x).toBeGreaterThanOrEqual(padding - 0.001);
      expect(p.x).toBeLessThanOrEqual(viewport.width - padding + 0.001);
      expect(p.y).toBeGreaterThanOrEqual(padding - 0.001);
      expect(p.y).toBeLessThanOrEqual(viewport.height - padding + 0.001);
    }
  });

  it('keeps the taste marker in the bounds so "you are here" is never clipped', () => {
    const farTaste: AtlasTaste = { x: 100, y: 100 };
    const fit = fitAtlas(stars, farTaste, viewport, padding);
    const p = atlasScreenOf(farTaste, fit, noZoom);
    expect(p.x).toBeLessThanOrEqual(viewport.width - padding + 0.001);
    expect(p.y).toBeLessThanOrEqual(viewport.height - padding + 0.001);
  });

  it('zoom spreads points away from the centre', () => {
    const fit = fitAtlas(stars, null, viewport, padding);
    const at1 = atlasScreenOf(star('a', -4, -5), fit, noZoom);
    const at2 = atlasScreenOf(star('a', -4, -5), fit, { ...noZoom, zoom: 2 });
    // Under 2x zoom about the centre, a point off-centre moves farther from the centre.
    const d1 = Math.hypot(at1.x - noZoom.centreX, at1.y - noZoom.centreY);
    const d2 = Math.hypot(at2.x - noZoom.centreX, at2.y - noZoom.centreY);
    expect(d2).toBeGreaterThan(d1);
  });
});

describe('starsNearTaste', () => {
  const stars = [star('near', 1, 1), star('mid', 5, 5), star('far', 20, 20)];

  it('is empty without a taste (anonymous or no vector yet)', () => {
    expect(starsNearTaste(stars, null, 2).size).toBe(0);
  });

  it('picks the closest stars to the taste in the plane', () => {
    const alive = starsNearTaste(stars, { x: 0, y: 0 }, 2);
    expect(alive.has('near')).toBe(true);
    expect(alive.has('mid')).toBe(true);
    expect(alive.has('far')).toBe(false);
  });

  it('never returns more than asked, nor more than exist', () => {
    expect(starsNearTaste(stars, { x: 0, y: 0 }, 1).size).toBe(1);
    expect(starsNearTaste(stars, { x: 0, y: 0 }, 99).size).toBe(3);
  });
});
