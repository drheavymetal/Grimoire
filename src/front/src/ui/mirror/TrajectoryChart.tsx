import { useTranslation } from 'react-i18next';
import { layoutTrajectory, polylinePoints } from '../../core/domain/trajectory';
import type { Trajectory } from '../../core/domain/types';

// The taste trajectory drawn on the Atlas plane (feature C16). The geometry is a pure core function
// (tested without a browser); this component only paints SVG primitives — the same discipline as the
// graph views, so it stays portable in spirit (react-native-svg accepts the same primitives).
export function TrajectoryChart({ trajectory }: { trajectory: Trajectory }) {
  const { t } = useTranslation();

  const width = 320;
  const height = 200;
  const layout = layoutTrajectory(trajectory.points, width, height);

  if (layout.points.length < 2) {
    return <p className="mt-3 font-mono text-xs text-muted">{t('mirror.trajectoryNotEnough')}</p>;
  }

  const path = polylinePoints(layout);
  const last = layout.points[layout.points.length - 1];

  return (
    <div>
      <svg
        viewBox={`0 0 ${width} ${height}`}
        className="mt-3 w-full max-w-md border border-line bg-panel"
        role="img"
        aria-label={t('mirror.trajectoryAria')}
      >
        <polyline points={path} fill="none" stroke="var(--muted)" strokeWidth={1.5} />
        {layout.points.map((p, i) => (
          <circle key={i} cx={p.cx} cy={p.cy} r={2.5} fill="var(--line)" />
        ))}
        {/* The current position is the sulphur star, "you are here now". */}
        <circle cx={last.cx} cy={last.cy} r={4} fill="var(--accent)" />
      </svg>
      <p className="mt-2 font-mono text-xs text-muted">
        {t('mirror.trajectoryDrift', { drift: trajectory.totalDrift.toFixed(3) })}
      </p>
    </div>
  );
}
