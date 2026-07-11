import { Component, type ErrorInfo, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

// Invariant 5 / R2: a graph that throws while laying out or painting — a pathological edge at
// scale, a d3-force blow-up, a NaN coordinate — must degrade locally to a designed empty state,
// never tear down the whole route. This class boundary catches the render error thrown by any
// GraphCanvas beneath it and shows a terse, unapologetic notice in the app's voice, so /explore (and
// every other page that hosts a graph) survives one broken graph. It lives in ui/ (invariant 6:
// error boundaries touch React internals, so they never belong in core/).

interface Props {
  children: ReactNode;
}

interface State {
  hasError: boolean;
}

export class GraphErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false };

  static getDerivedStateFromError(): State {
    return { hasError: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    // The graph is never load-bearing for its route, so we swallow the crash — but we do NOT
    // silence it (no empty catch, per the review gate): surface it to the console and carry on.
    console.error('GraphCanvas failed to render', error, info);
  }

  render(): ReactNode {
    if (this.state.hasError) {
      return <GraphErrorFallback />;
    }
    return this.props.children;
  }
}

// Kept private (not exported) so the file exports a single component — react-refresh stays happy —
// and so the fallback can read the i18n catalogs through the hook the class cannot use.
function GraphErrorFallback() {
  const { t } = useTranslation();
  return (
    <div className="mt-3 border border-line border-dashed p-6 text-center">
      <p className="font-mono text-xs uppercase text-muted">{t('graph.renderError')}</p>
    </div>
  );
}
