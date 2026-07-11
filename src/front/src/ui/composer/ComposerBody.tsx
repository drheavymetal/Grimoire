import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useComposer } from '../../core/hooks/useComposer';
import type { ArtistDetail, ComposerDetail, ComposerLink, WorkGroup } from '../../core/domain/types';
import { RankedName } from '../RankedName';
import { GraphCanvas } from '../graph/GraphCanvas';

// The composer body (movement VII, D11). A composer is NOT a band: no Gantt, no members, no rank
// (classical listeners lie). The hero is the grouped list of works; below it, the two lineages
// (teacher/student and influence) as clickable lists plus the shared lineage graph. Identity comes
// from the ArtistDetail the page already holds; the works and lineage load through useComposer.
export function ComposerBody({ data }: { data: ArtistDetail }) {
  const { t } = useTranslation();
  const { data: composer, isLoading, isError } = useComposer(data.id, true);

  return (
    <article>
      <BackLink />

      {/* The composer's name renders in the crisp base Redaction cut: composers have no rank
          (D11), and an unknown rank is never corroded (D35/D38). No invented degradation. */}
      <h1 className="mt-3">
        <RankedName name={data.name} rank={data.rank} className="text-5xl text-strong" />
      </h1>

      <dl className="mt-4 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 font-mono text-xs text-muted">
        <dt className="uppercase">{t('artist.origin')}</dt>
        <dd className="text-strong">
          {data.country ?? '—'}
          {data.city ? ` · ${data.city}` : ''}
        </dd>
      </dl>

      <section className="mt-6">
        <h2 className="font-mono text-xs uppercase text-muted">{t('artist.tags')}</h2>
        {data.tags.length > 0 ? (
          <ul className="mt-2 flex flex-wrap gap-2">
            {data.tags.map((tag) => (
              <li key={tag} className="border border-line px-2 py-1 font-mono text-xs text-strong">
                {tag}
              </li>
            ))}
          </ul>
        ) : (
          <p className="mt-2 font-mono text-xs text-muted">{t('artist.noTags')}</p>
        )}
      </section>

      <section className="mt-8">
        <h2 className="font-mono text-xs uppercase text-muted">{t('artist.bio')}</h2>
        {data.abstract !== null && data.abstract.trim().length > 0 ? (
          <p className="mt-2 max-w-prose font-body leading-relaxed text-strong">{data.abstract}</p>
        ) : (
          <p className="mt-2 font-mono text-xs text-muted">{t('artist.noBio')}</p>
        )}
      </section>

      {/* The hero: works grouped by kind. */}
      <section className="mt-10">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h2 className="font-display text-2xl text-strong">{t('composer.worksTitle')}</h2>
          {composer !== undefined ? (
            <span className="font-mono text-xs uppercase text-muted">
              {t('composer.workCount', { count: composer.workCount })}
            </span>
          ) : null}
        </div>
        <p className="mt-1 font-mono text-xs text-muted">{t('composer.worksHint')}</p>

        {isLoading ? (
          <p className="mt-3 font-mono text-sm text-muted">{t('composer.loading')}</p>
        ) : isError ? (
          <p className="mt-3 font-mono text-sm text-danger">{t('composer.error')}</p>
        ) : composer !== undefined ? (
          composer.workGroups.length > 0 ? (
            <div className="mt-3 space-y-5">
              {composer.workGroups.map((group) => (
                <WorkGroupBlock key={group.kind ?? '\0unclassified'} group={group} />
              ))}
            </div>
          ) : (
            <p className="mt-3 font-mono text-xs text-muted">{t('composer.noWorks')}</p>
          )
        ) : null}
      </section>

      {/* The lineage: teacher/student and influence. */}
      {composer !== undefined ? <ComposerLineageView composer={composer} /> : null}
    </article>
  );
}

function WorkGroupBlock({ group }: { group: WorkGroup }) {
  const { t } = useTranslation();
  const heading = group.kind ?? t('composer.unclassified');

  return (
    <div>
      <h3 className="font-mono text-xs uppercase text-accent">{heading}</h3>
      <ul className="mt-2 space-y-1">
        {group.works.map((work) => (
          <li key={work.id} className="flex items-baseline gap-3 border-b border-line pb-1">
            <span className="min-w-0 flex-1 font-body text-strong">{work.title}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function ComposerLineageView({ composer }: { composer: ComposerDetail }) {
  const { t } = useTranslation();
  const { teachers, students, influences, graph } = composer.lineage;
  const hasLineage =
    teachers.length > 0 ||
    students.length > 0 ||
    influences.length > 0 ||
    graph.nodes.length > 0;

  return (
    <section className="mt-10">
      <h2 className="font-display text-2xl text-strong">{t('composer.lineageTitle')}</h2>
      <p className="mt-1 font-mono text-xs text-muted">{t('composer.lineageHint')}</p>

      {hasLineage ? (
        <>
          <div className="mt-4 grid gap-4 sm:grid-cols-3">
            <LinkColumn title={t('composer.studiedWith')} links={teachers} />
            <LinkColumn title={t('composer.taught')} links={students} />
            <LinkColumn title={t('composer.influencedBy')} links={influences} />
          </div>
          {/* The same relations as the shared lineage graph (D18): the ego in sulphur, the
              pedagogical chain solid, influence dashed. Clicking a node opens that composer. */}
          {graph.nodes.length > 0 ? <GraphCanvas graph={graph} height={360} /> : null}
        </>
      ) : (
        <p className="mt-3 font-mono text-xs text-muted">{t('composer.noLineage')}</p>
      )}
    </section>
  );
}

function LinkColumn({ title, links }: { title: string; links: ComposerLink[] }) {
  if (links.length === 0) {
    return null;
  }

  return (
    <div>
      <h3 className="font-mono text-[0.6rem] uppercase text-muted">{title}</h3>
      <ul className="mt-1 space-y-0.5">
        {links.map((link) => (
          <li key={link.id} className="font-body text-sm text-strong">
            <Link
              to="/artist/$artistId"
              params={{ artistId: link.id }}
              className="no-underline hover:text-accent"
            >
              {link.name}
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}

function BackLink() {
  const { t } = useTranslation();
  return (
    <Link to="/" className="font-mono text-xs uppercase text-muted no-underline hover:text-accent">
      ← {t('artist.back')}
    </Link>
  );
}
