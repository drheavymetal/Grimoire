import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type PointerEvent as ReactPointerEvent,
  type WheelEvent as ReactWheelEvent,
} from 'react';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { atlasScreenOf, fitAtlas, type AtlasPoint } from '../../core/domain/atlas';
import type { Atlas } from '../../core/domain/types';
import { useMeasuredWidth } from '../lineup/useMeasuredWidth';

// The Atlas (C18/B22) is the ONE view that renders to a canvas, the explicit exception to
// invariant 6 (D18/D24): ~300k nodes at full scale have no reasonable SVG/React-Native port, so
// this component lives only in ui/ and paints imperatively. All coordinate math — the fit, the
// screen mapping, "which stars are near you" — stays pure and DOM-free in core/domain/atlas.ts and
// is tested without a canvas; this file only reads those results and draws pixels. Empty regions
// between the clusters are the gaps (B23).

const HEIGHT = 520;
const PADDING = 44;
const MIN_ZOOM = 0.5;
const MAX_ZOOM = 8;
const HIT_RADIUS = 10;

interface Colors {
  bg: string;
  strong: string;
  muted: string;
  accent: string;
}

function readColors(element: HTMLElement): Colors {
  const style = getComputedStyle(element);
  return {
    bg: style.getPropertyValue('--color-bg').trim() || '#000',
    strong: style.getPropertyValue('--color-strong').trim() || '#fff',
    muted: style.getPropertyValue('--color-muted').trim() || '#888',
    accent: style.getPropertyValue('--color-accent').trim() || '#8f7c18',
  };
}

interface Props {
  atlas: Atlas;
  aliveIds: Set<string>;
}

export function AtlasCanvas({ atlas, aliveIds }: Props) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [containerRef, measuredWidth] = useMeasuredWidth<HTMLDivElement>();
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [themeTick, setThemeTick] = useState(0);
  const dragRef = useRef<{ x: number; y: number; moved: boolean } | null>(null);

  const width = measuredWidth > 0 ? measuredWidth : 640;
  const fit = useMemo(
    () => fitAtlas(atlas.stars, atlas.taste, { width, height: HEIGHT }, PADDING),
    [atlas.stars, atlas.taste, width],
  );

  const view = useMemo(
    () => ({ zoom, panX: pan.x, panY: pan.y, centreX: width / 2, centreY: HEIGHT / 2 }),
    [zoom, pan.x, pan.y, width],
  );

  const screenOf = useMemo(
    () => (point: AtlasPoint): AtlasPoint => atlasScreenOf(point, fit, view),
    [fit, view],
  );

  // Redraw when the theme flips (canvas cannot read CSS variables live): watch the root class.
  useEffect(() => {
    const root = document.documentElement;
    const observer = new MutationObserver(() => setThemeTick((n) => n + 1));
    observer.observe(root, { attributes: true, attributeFilter: ['class'] });
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    const canvas = canvasRef.current;
    const container = containerRef.current;
    if (canvas === null || container === null) {
      return;
    }

    const ctx = canvas.getContext('2d');
    if (ctx === null) {
      return;
    }

    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    canvas.width = Math.round(width * dpr);
    canvas.height = Math.round(HEIGHT * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

    const colors = readColors(container);

    ctx.fillStyle = colors.bg;
    ctx.fillRect(0, 0, width, HEIGHT);

    // Nebula: soft additive glows accumulate where stars cluster, so density reads as light and the
    // voids between clusters read as the gaps (B23). Near-taste glows are sulphur; the rest are bone.
    ctx.globalCompositeOperation = 'lighter';
    for (const star of atlas.stars) {
      const p = screenOf(star);
      const alive = aliveIds.has(star.id);
      const r = alive ? 26 : 18;
      const gradient = ctx.createRadialGradient(p.x, p.y, 0, p.x, p.y, r);
      gradient.addColorStop(0, alive ? colors.accent : colors.muted);
      gradient.addColorStop(1, 'transparent');
      ctx.globalAlpha = alive ? 0.28 : 0.09;
      ctx.fillStyle = gradient;
      ctx.beginPath();
      ctx.arc(p.x, p.y, r, 0, Math.PI * 2);
      ctx.fill();
    }

    // Stars: crisp points over the nebula. Alive = sulphur and larger; the rest are faint bone.
    ctx.globalCompositeOperation = 'source-over';
    ctx.globalAlpha = 1;
    for (const star of atlas.stars) {
      const p = screenOf(star);
      const alive = aliveIds.has(star.id);
      ctx.beginPath();
      ctx.arc(p.x, p.y, alive ? 2.6 : 1.5, 0, Math.PI * 2);
      ctx.fillStyle = alive ? colors.accent : colors.strong;
      ctx.globalAlpha = alive ? 1 : 0.5;
      ctx.fill();
    }

    // "You are here": the taste marker, a sulphur ring at the projected taste position.
    if (atlas.taste !== null) {
      const p = screenOf(atlas.taste);
      ctx.globalAlpha = 1;
      ctx.strokeStyle = colors.accent;
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.arc(p.x, p.y, 7, 0, Math.PI * 2);
      ctx.stroke();
      ctx.beginPath();
      ctx.arc(p.x, p.y, 1.6, 0, Math.PI * 2);
      ctx.fillStyle = colors.accent;
      ctx.fill();
    }
    // themeTick forces a redraw on theme change; it is a dependency, not used in the body.
  }, [atlas.stars, atlas.taste, aliveIds, screenOf, width, themeTick, containerRef]);

  function nearestStarId(clientX: number, clientY: number): string | null {
    const canvas = canvasRef.current;
    if (canvas === null) {
      return null;
    }
    const rect = canvas.getBoundingClientRect();
    const x = clientX - rect.left;
    const y = clientY - rect.top;

    let bestId: string | null = null;
    let bestDistSq = HIT_RADIUS * HIT_RADIUS;
    for (const star of atlas.stars) {
      const p = screenOf(star);
      const dSq = (p.x - x) ** 2 + (p.y - y) ** 2;
      if (dSq <= bestDistSq) {
        bestDistSq = dSq;
        bestId = star.id;
      }
    }
    return bestId;
  }

  const onWheel = (event: ReactWheelEvent<HTMLCanvasElement>): void => {
    event.preventDefault();
    const factor = event.deltaY < 0 ? 1.15 : 1 / 1.15;
    setZoom((z) => Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, z * factor)));
  };

  const onPointerDown = (event: ReactPointerEvent<HTMLCanvasElement>): void => {
    dragRef.current = { x: event.clientX - pan.x, y: event.clientY - pan.y, moved: false };
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const onPointerMove = (event: ReactPointerEvent<HTMLCanvasElement>): void => {
    if (dragRef.current === null) {
      return;
    }
    dragRef.current.moved = true;
    setPan({ x: event.clientX - dragRef.current.x, y: event.clientY - dragRef.current.y });
  };

  const onPointerUp = (event: ReactPointerEvent<HTMLCanvasElement>): void => {
    const drag = dragRef.current;
    dragRef.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
    // A click that did not pan opens the nearest star's page (its ficha).
    if (drag !== null && !drag.moved) {
      const id = nearestStarId(event.clientX, event.clientY);
      if (id !== null) {
        void navigate({ to: '/artist/$artistId', params: { artistId: id } });
      }
    }
  };

  const resetView = (): void => {
    setZoom(1);
    setPan({ x: 0, y: 0 });
  };

  if (atlas.stars.length === 0) {
    return (
      <div className="mt-4 border border-dashed border-line p-8 text-center">
        <p className="font-display text-xl text-strong">{t('atlas.emptyTitle')}</p>
        <p className="mx-auto mt-2 max-w-prose font-body text-sm text-muted">{t('atlas.emptyBody')}</p>
      </div>
    );
  }

  return (
    <div className="mt-4">
      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          onClick={resetView}
          className="font-mono text-xs uppercase text-muted hover:text-accent"
        >
          {t('atlas.reset')}
        </button>
        <span className="font-mono text-[0.65rem] uppercase text-muted">{t('atlas.hint')}</span>
      </div>

      <div ref={containerRef} className="mt-2 w-full overflow-hidden border border-line">
        <canvas
          ref={canvasRef}
          role="img"
          aria-label={t('atlas.aria', { count: atlas.stars.length })}
          style={{ display: 'block', width: '100%', height: HEIGHT, cursor: dragRef.current?.moved ? 'grabbing' : 'crosshair', touchAction: 'none' }}
          onWheel={onWheel}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerLeave={onPointerUp}
        />
      </div>
    </div>
  );
}
