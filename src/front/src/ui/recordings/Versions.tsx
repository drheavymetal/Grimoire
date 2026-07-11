import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useArtistVersions } from '../../core/hooks/useRecordings';
import type { CoverEdge, Graph } from '../../core/domain/types';
import { GraphCanvas } from '../graph/GraphCanvas';
import { GraphErrorBoundary } from '../GraphErrorBoundary';

// C10 — the version graph ("quién versionó a quién"): every CROSS-ARTIST cover touching this band's
// recordings — the band covered, or covering someone else. Own remixes/remasters are filtered out
// server-side (they are not the "someone else" story). The topology is the shared graph engine
// (D18) inside its error boundary, with the cover relation drawn on each edge; the companion list
// gives the covered song each edge stands for, which the graph cannot carry. Most of the underground
// has no cross-artist cover, so the empty state is designed and unapologetic (R2).
export function Versions({ artistId }: { artistId: string }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useArtistVersions(artistId);

  const relationLabel = (relation: string): string =>
    t(`versionRelation.${relation}`, { defaultValue: relation });

  return (
    <section className="mt-10">
      <h2 className="font-display text-2xl text-strong">{t('versions.title')}</h2>
      <p className="mt-1 font-mono text-xs text-muted">{t('versions.hint')}</p>

      {isLoading ? (
        <p className="mt-3 font-mono text-sm text-muted">{t('versions.loading')}</p>
      ) : isError ? (
        <p className="mt-3 font-mono text-sm text-danger">{t('versions.error')}</p>
      ) : data === undefined || data.versions.length === 0 ? (
        <div className="mt-3 border border-line border-dashed p-6 text-center">
          <p className="font-body text-sm text-muted">{t('versions.empty')}</p>
        </div>
      ) : (
        <div className="mt-3">
          <GraphErrorBoundary>
            <GraphCanvas graph={translateEdgeLabels(data.graph, relationLabel)} height={360} showEdgeLabels />
          </GraphErrorBoundary>
          <ul className="mt-4 divide-y divide-line border-y border-line">
            {data.versions.map((version, i) => (
              <VersionRow key={`${version.title}-${i}`} version={version} relationLabel={relationLabel} />
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}

// One cover in the companion list: original band → covering band, the relation, and the song.
function VersionRow({
  version,
  relationLabel,
}: {
  version: CoverEdge;
  relationLabel: (relation: string) => string;
}) {
  return (
    <li className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 py-2 font-body text-sm text-strong">
      <Link
        to="/artist/$artistId"
        params={{ artistId: version.originalArtistId }}
        className="no-underline hover:text-accent"
      >
        {version.originalArtistName}
      </Link>
      <span className="font-mono text-xs text-muted">→</span>
      <Link
        to="/artist/$artistId"
        params={{ artistId: version.coverArtistId }}
        className="no-underline hover:text-accent"
      >
        {version.coverArtistName}
      </Link>
      <span className="font-mono text-[0.6rem] uppercase text-accent">{relationLabel(version.relation)}</span>
      <span className="min-w-0 basis-full truncate font-mono text-xs text-muted">{version.title}</span>
    </li>
  );
}

// Returns a copy of the graph with each edge's label run through the relation translator, so the
// edge annotations read in the viewer's language while the payload stays untouched (invariant 6:
// this is a ui/ concern, not core/).
function translateEdgeLabels(graph: Graph, relationLabel: (relation: string) => string): Graph {
  return {
    nodes: graph.nodes,
    edges: graph.edges.map((edge) => ({
      ...edge,
      label: edge.label !== null ? relationLabel(edge.label) : null,
    })),
  };
}
