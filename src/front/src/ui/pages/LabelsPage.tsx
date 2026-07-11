import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useLabels } from '../../core/hooks/useLabels';
import { PageHeader } from '../PageHeader';

// B21 — labels as a door. The way people actually find metal: trust a label, walk its roster.
// Real labels the ETL resolved; a thin index renders a designed empty state.

export function LabelsPage() {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useLabels();

  const labels = data ?? [];

  return (
    <section>
      <PageHeader
        eyebrow={t('labels.eyebrow')}
        title={t('labels.heading')}
        lead={<p className="font-mono text-xs text-muted">{t('labels.intro')}</p>}
      />

      {isLoading ? <p className="mt-6 font-mono text-sm text-muted">{t('labels.loading')}</p> : null}
      {isError ? <p className="mt-6 font-mono text-sm text-danger">{t('labels.error')}</p> : null}

      {!isLoading && !isError && labels.length === 0 ? (
        <div className="mt-6 border border-line border-dashed p-8 text-center">
          <p className="font-body text-sm text-muted">{t('labels.empty')}</p>
        </div>
      ) : null}

      {labels.length > 0 ? (
        <ul className="mt-6 divide-y divide-line border-y border-line">
          {labels.map((label) => (
            <li key={label.id}>
              <Link
                to="/label/$labelId"
                params={{ labelId: label.id }}
                className="flex items-baseline justify-between gap-4 py-3 no-underline"
              >
                <span className="font-display text-xl text-strong">{label.name}</span>
                <span className="shrink-0 font-mono text-xs text-muted">
                  {label.country ?? '—'} · {t('labels.releaseCount', { count: label.releaseCount })}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}
