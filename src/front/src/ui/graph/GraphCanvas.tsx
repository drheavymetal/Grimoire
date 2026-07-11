import { useMemo, useRef, useState, type PointerEvent as ReactPointerEvent, type WheelEvent as ReactWheelEvent } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import {
  fitToViewport,
  layoutGraph,
  shouldShowLabel,
  transformPoint,
} from '../../core/domain/graph';
import type { Graph, GraphNode } from '../../core/domain/types';
import { useMeasuredWidth } from '../lineup/useMeasuredWidth';

// The shared lineage graph painter (D18 / SPEC §9). It takes a graph, asks core/ for a headless
// force layout, and paints it with SVG primitives only — <line>, <circle>, <text> — which
// react-native-svg accepts unchanged. Auto-fit transforms the layout positions in JS (never
// scaling a <g>); the user's zoom spreads those screen positions while glyphs stay a constant
// pixel size (the contra-scale by 1/k is implicit because we place, not scale). Labels appear on
// focus, on a search match, or once zoomed in past the threshold.

interface Props {
  graph: Graph;
  height?: number;
  onNodeClick?: (id: string) => void;
}

const PADDING = 36;
const MIN_ZOOM = 0.4;
const MAX_ZOOM = 4;

export function GraphCanvas({ graph, height = 440, onNodeClick }: Props) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [containerRef, measuredWidth] = useMeasuredWidth<HTMLDivElement>();

  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [focusedId, setFocusedId] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const dragRef = useRef<{ x: number; y: number } | null>(null);

  const layout = useMemo(() => layoutGraph(graph), [graph]);

  const width = measuredWidth > 0 ? measuredWidth : 640;
  const fit = fitToViewport(layout.bounds, { width, height }, PADDING);
  const cx = width / 2;
  const cy = height / 2;

  const q = query.trim().toLowerCase();

  const goToNode = (id: string): void => {
    if (onNodeClick) {
      onNodeClick(id);
      return;
    }
    void navigate({ to: '/artist/$artistId', params: { artistId: id } });
  };

  // Screen position of a node: fit → then the user's zoom about the viewport centre, plus pan.
  const screenOf = (node: { x: number; y: number }): { x: number; y: number } => {
    const base = transformPoint(node.x, node.y, fit);
    return {
      x: (base.x - cx) * zoom + cx + pan.x,
      y: (base.y - cy) * zoom + cy + pan.y,
    };
  };

  const positions = new Map<string, { x: number; y: number }>();
  for (const node of layout.nodes) {
    positions.set(node.id, screenOf(node));
  }

  const onWheel = (event: ReactWheelEvent<SVGSVGElement>): void => {
    event.preventDefault();
    const factor = event.deltaY < 0 ? 1.15 : 1 / 1.15;
    setZoom((z) => Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, z * factor)));
  };

  const onPointerDown = (event: ReactPointerEvent<SVGSVGElement>): void => {
    dragRef.current = { x: event.clientX - pan.x, y: event.clientY - pan.y };
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const onPointerMove = (event: ReactPointerEvent<SVGSVGElement>): void => {
    if (dragRef.current === null) {
      return;
    }
    setPan({ x: event.clientX - dragRef.current.x, y: event.clientY - dragRef.current.y });
  };

  const onPointerUp = (event: ReactPointerEvent<SVGSVGElement>): void => {
    dragRef.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
  };

  const resetView = (): void => {
    setZoom(1);
    setPan({ x: 0, y: 0 });
  };

  if (graph.nodes.length === 0) {
    return (
      <div className="mt-3 border border-line border-dashed p-6 text-center">
        <p className="font-mono text-xs uppercase text-muted">{t('graph.empty')}</p>
      </div>
    );
  }

  return (
    <div className="mt-3">
      <div className="flex flex-wrap items-center gap-3">
        <input
          type="search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder={t('graph.searchPlaceholder')}
          className="w-48 border border-line bg-panel px-3 py-1.5 font-mono text-xs text-strong outline-none focus:border-accent"
          autoComplete="off"
        />
        <button
          type="button"
          onClick={resetView}
          className="font-mono text-xs uppercase text-muted hover:text-accent"
        >
          {t('graph.reset')}
        </button>
        <span className="font-mono text-[0.65rem] uppercase text-muted">{t('graph.hint')}</span>
      </div>

      <div ref={containerRef} className="mt-2 w-full overflow-hidden border border-line">
        <svg
          width={width}
          height={height}
          className="block touch-none text-strong"
          style={{ cursor: dragRef.current ? 'grabbing' : 'grab' }}
          role="group"
          aria-label={t('graph.aria', { count: layout.nodes.length })}
          onWheel={onWheel}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerLeave={onPointerUp}
        >
          {/* Edges first, under the nodes. Member = solid faint line; influence = dashed accent. */}
          {graph.edges.map((edge, i) => {
            const a = positions.get(edge.source);
            const b = positions.get(edge.target);
            if (a === undefined || b === undefined) {
              return null;
            }
            const influence = edge.kind === 'influence';
            return (
              <line
                key={`e-${edge.source}-${edge.target}-${i}`}
                x1={a.x}
                y1={a.y}
                x2={b.x}
                y2={b.y}
                stroke={influence ? 'var(--color-accent)' : 'currentColor'}
                strokeOpacity={influence ? 0.55 : 0.25}
                strokeWidth={1}
                strokeDasharray={influence ? '4 3' : undefined}
              />
            );
          })}

          {/* Nodes. Bands are filled circles; people are smaller hollow circles. Ego/source/target
              are drawn in sulphur — the only accent (DESIGN §5). No rank-driven type here (Q1). */}
          {layout.nodes.map((node) => {
            const p = positions.get(node.id);
            if (p === undefined) {
              return null;
            }
            return (
              <GraphNodeGlyph
                key={node.id}
                node={node}
                x={p.x}
                y={p.y}
                labelled={shouldShowLabel({
                  focused: focusedId === node.id,
                  matched: q.length > 0 && node.name.toLowerCase().includes(q),
                  zoom,
                })}
                dimmed={q.length > 0 && !node.name.toLowerCase().includes(q)}
                onActivate={() => goToNode(node.id)}
                onFocusNode={() => setFocusedId(node.id)}
                onBlurNode={() => setFocusedId((id) => (id === node.id ? null : id))}
              />
            );
          })}
        </svg>
      </div>
    </div>
  );
}

interface GlyphProps {
  node: GraphNode;
  x: number;
  y: number;
  labelled: boolean;
  dimmed: boolean;
  onActivate: () => void;
  onFocusNode: () => void;
  onBlurNode: () => void;
}

function GraphNodeGlyph({ node, x, y, labelled, dimmed, onActivate, onFocusNode, onBlurNode }: GlyphProps) {
  const isBand = node.kind === 'Group';
  const special = node.role !== 'node';
  const radius = isBand ? 7 : 5;
  const fill = special ? 'var(--color-accent)' : isBand ? 'currentColor' : 'var(--color-bg)';
  const stroke = special ? 'var(--color-accent)' : 'currentColor';

  return (
    <g
      transform={`translate(${x} ${y})`}
      style={{ opacity: dimmed ? 0.2 : 1, cursor: 'pointer', outline: 'none' }}
      tabIndex={0}
      role="button"
      aria-label={node.name}
      onClick={onActivate}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          onActivate();
        }
      }}
      onMouseEnter={onFocusNode}
      onMouseLeave={onBlurNode}
      onFocus={onFocusNode}
      onBlur={onBlurNode}
    >
      <circle
        r={radius}
        fill={fill}
        stroke={stroke}
        strokeWidth={isBand ? 0 : 1.5}
        fillOpacity={special || isBand ? 0.9 : 1}
      />
      {labelled ? (
        <text
          x={radius + 4}
          y={0}
          dominantBaseline="central"
          className="font-mono"
          fontSize={11}
          fill="currentColor"
          style={{ pointerEvents: 'none' }}
        >
          {node.name}
        </text>
      ) : null}
    </g>
  );
}
