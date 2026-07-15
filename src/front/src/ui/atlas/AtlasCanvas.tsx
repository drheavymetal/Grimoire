import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type PointerEvent as ReactPointerEvent,
  type WheelEvent as ReactWheelEvent,
} from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { atlasScreenOf, fitAtlas, type AtlasPoint } from '../../core/domain/atlas';
import type { Atlas, AtlasStar } from '../../core/domain/types';
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
  danger: string;
}

function readColors(element: HTMLElement): Colors {
  const style = getComputedStyle(element);
  return {
    bg: style.getPropertyValue('--color-bg').trim() || '#000',
    strong: style.getPropertyValue('--color-strong').trim() || '#fff',
    muted: style.getPropertyValue('--color-muted').trim() || '#888',
    accent: style.getPropertyValue('--color-accent').trim() || '#8f7c18',
    danger: style.getPropertyValue('--color-danger').trim() || '#c0392b',
  };
}

interface Props {
  atlas: Atlas;
  aliveIds: Set<string>;
  // The FRIENDS wave: a friend's taste projected into the same plane, drawn as a distinct
  // danger-coloured diamond with its own legend entry. Absent/null = no friend overlaid (the
  // default, so the plain Atlas page is unaffected). Only drawn when both coords are finite.
  friendPoint?: AtlasPoint | null;
  friendLabel?: string;
}

export function AtlasCanvas({ atlas, aliveIds, friendPoint = null, friendLabel }: Props) {
  const { t } = useTranslation();
  const [containerRef, measuredWidth] = useMeasuredWidth<HTMLDivElement>();
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [themeTick, setThemeTick] = useState(0);
  // The star under the cursor (transient, follows the mouse) and the one the user clicked to pin.
  // Hovering reads a group without opening it; a click pins a card so a stray click never yanks you
  // off the map — navigation is only the explicit "open" link on the card.
  const [hovered, setHovered] = useState<AtlasStar | null>(null);
  const [pinned, setPinned] = useState<AtlasStar | null>(null);
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

    // A friend's taste (the FRIENDS wave): a danger-coloured diamond ring, deliberately a different
    // shape and hue from your own sulphur ring so the two are never confused. Only when placeable.
    if (friendPoint !== null && Number.isFinite(friendPoint.x) && Number.isFinite(friendPoint.y)) {
      const p = screenOf(friendPoint);
      ctx.globalAlpha = 1;
      ctx.strokeStyle = colors.danger;
      ctx.fillStyle = colors.danger;
      ctx.lineWidth = 1.5;
      // Diamond ring.
      const r = 7;
      ctx.beginPath();
      ctx.moveTo(p.x, p.y - r);
      ctx.lineTo(p.x + r, p.y);
      ctx.lineTo(p.x, p.y + r);
      ctx.lineTo(p.x - r, p.y);
      ctx.closePath();
      ctx.stroke();
      ctx.beginPath();
      ctx.arc(p.x, p.y, 1.6, 0, Math.PI * 2);
      ctx.fill();
      if (friendLabel !== undefined && friendLabel.length > 0) {
        ctx.font = '11px ui-monospace, monospace';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'bottom';
        ctx.fillText(friendLabel, p.x, p.y - r - 4);
      }
    }
    // themeTick forces a redraw on theme change; it is a dependency, not used in the body.
  }, [atlas.stars, atlas.taste, aliveIds, screenOf, width, themeTick, containerRef, friendPoint, friendLabel]);

  function nearestStar(clientX: number, clientY: number): AtlasStar | null {
    const canvas = canvasRef.current;
    if (canvas === null) {
      return null;
    }
    const rect = canvas.getBoundingClientRect();
    const x = clientX - rect.left;
    const y = clientY - rect.top;

    let best: AtlasStar | null = null;
    let bestDistSq = HIT_RADIUS * HIT_RADIUS;
    for (const star of atlas.stars) {
      const p = screenOf(star);
      const dSq = (p.x - x) ** 2 + (p.y - y) ** 2;
      if (dSq <= bestDistSq) {
        bestDistSq = dSq;
        best = star;
      }
    }
    return best;
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
    // Panning: drag the field and suppress the hover label until the drag ends.
    if (dragRef.current !== null) {
      dragRef.current.moved = true;
      setPan({ x: event.clientX - dragRef.current.x, y: event.clientY - dragRef.current.y });
      if (hovered !== null) {
        setHovered(null);
      }
      return;
    }
    // Not dragging: read the nearest star under the cursor. Only touch state when the id changes so a
    // plain mouse move does not re-render on every pixel.
    const star = nearestStar(event.clientX, event.clientY);
    setHovered((prev) => (prev?.id === (star?.id ?? null) ? prev : star));
  };

  const onPointerUp = (event: ReactPointerEvent<HTMLCanvasElement>): void => {
    const drag = dragRef.current;
    dragRef.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
    // A click that did not pan pins the nearest star's card (or clears the pin on empty space). It no
    // longer navigates — that would drop you out of the Atlas; the card carries the explicit link.
    if (drag !== null && !drag.moved) {
      setPinned(nearestStar(event.clientX, event.clientY));
    }
  };

  const onPointerLeave = (event: ReactPointerEvent<HTMLCanvasElement>): void => {
    // Leaving the canvas ends any drag and drops the hover label, but never pins or clears the card.
    dragRef.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
    setHovered(null);
  };

  const resetView = (): void => {
    setZoom(1);
    setPan({ x: 0, y: 0 });
  };

  // Screen positions for the HTML overlays, recomputed each render so they track pan and zoom.
  const hoveredPos = hovered !== null ? screenOf(hovered) : null;
  const pinnedPos = pinned !== null ? screenOf(pinned) : null;

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

      {/* The frame is an instrument panel over the void, not a bare canvas: a corner sigil, a soft
          edge vignette so the star field reads as depth fading into the dark, and a Courier legend.
          The overlays are pointer-events-none so they never intercept a pan or a click-through. */}
      <div ref={containerRef} className="relative mt-2 w-full overflow-hidden border border-line">
        <canvas
          ref={canvasRef}
          role="img"
          aria-label={t('atlas.aria', { count: atlas.stars.length })}
          style={{ display: 'block', width: '100%', height: HEIGHT, cursor: dragRef.current?.moved ? 'grabbing' : hovered !== null ? 'pointer' : 'crosshair', touchAction: 'none' }}
          onWheel={onWheel}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerLeave={onPointerLeave}
        />
        <div
          aria-hidden="true"
          className="pointer-events-none absolute inset-0"
          style={{ background: 'radial-gradient(120% 90% at 50% 40%, transparent 52%, color-mix(in srgb, var(--color-bg) 82%, transparent) 100%)' }}
        />

        {/* Hover label: the group's name floating above its star, so the field can be read by moving
            the cursor. Hidden while a card is pinned on the same star (the card already names it). */}
        {hoveredPos !== null && hovered !== null && hovered.id !== pinned?.id ? (
          <div
            className="pointer-events-none absolute z-10 whitespace-nowrap border border-line px-2 py-1 font-mono text-[0.7rem] text-strong"
            style={{
              left: hoveredPos.x,
              top: hoveredPos.y,
              transform: 'translate(-50%, calc(-100% - 10px))',
              background: 'color-mix(in srgb, var(--color-bg) 92%, transparent)',
            }}
          >
            {hovered.name}
          </div>
        ) : null}

        {/* Pinned card: a click parks the group here with its rank and an explicit link, instead of
            navigating away and losing the map. Flips to the other side of the point near the edge. */}
        {pinnedPos !== null && pinned !== null ? (
          <div
            className="pointer-events-auto absolute z-20 w-max max-w-[220px] border border-line p-3 shadow-lg"
            style={{
              left: pinnedPos.x,
              top: pinnedPos.y,
              transform: `translate(${pinnedPos.x > width / 2 ? 'calc(-100% - 12px)' : '12px'}, -50%)`,
              background: 'var(--color-bg)',
            }}
          >
            <button
              type="button"
              onClick={() => setPinned(null)}
              aria-label={t('atlas.close')}
              className="absolute right-1.5 top-1.5 font-mono text-xs text-muted hover:text-accent"
            >
              ✕
            </button>
            <p className="pr-4 font-display text-base text-strong">{pinned.name}</p>
            {pinned.rank !== null ? (
              <p className="mt-0.5 font-mono text-[0.6rem] uppercase tracking-[0.16em] text-muted">
                {t(`rank.${pinned.rank}`)}
              </p>
            ) : null}
            <Link
              to="/artist/$artistId"
              params={{ artistId: pinned.id }}
              className="mt-2 inline-block font-mono text-xs text-accent no-underline hover:text-strong"
            >
              {t('atlas.openFiche')}
            </Link>
          </div>
        ) : null}
      </div>

      <div className="mt-2 flex flex-wrap items-center gap-x-5 gap-y-1 font-mono text-[0.62rem] uppercase tracking-[0.16em] text-muted">
        <span className="inline-flex items-center gap-1.5">
          <span aria-hidden="true" className="inline-block h-1.5 w-1.5 rounded-full bg-strong opacity-60" />
          {t('atlas.legendField')}
        </span>
        <span className="inline-flex items-center gap-1.5">
          <span aria-hidden="true" className="inline-block h-1.5 w-1.5 rounded-full bg-accent" />
          {t('atlas.legendTaste')}
        </span>
        {friendPoint !== null && Number.isFinite(friendPoint.x) && Number.isFinite(friendPoint.y) ? (
          <span className="inline-flex items-center gap-1.5">
            <span aria-hidden="true" className="inline-block h-1.5 w-1.5 rotate-45 bg-danger" />
            {friendLabel !== undefined && friendLabel.length > 0
              ? t('atlas.legendFriendNamed', { name: friendLabel })
              : t('atlas.legendFriend')}
          </span>
        ) : null}
      </div>
    </div>
  );
}
