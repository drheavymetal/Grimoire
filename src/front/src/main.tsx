import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { I18nextProvider } from 'react-i18next';

import '@fontsource/redaction/400.css';
import '@fontsource/redaction/700.css';
import '@fontsource/archivo/400.css';
import '@fontsource/archivo/600.css';
import '@fontsource/courier-prime/400.css';
import '@fontsource/courier-prime/700.css';
import './styles.css';

import i18n from './i18n';
import { createGrimoireClient } from './core/api/client';
import { GrimoireClientProvider } from './core/api/context';
import { router } from './ui/routes';

// The base URL is resolved here, in the web entry point, and injected into core.
const apiBaseUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:5080';
const grimoireClient = createGrimoireClient(apiBaseUrl);
const queryClient = new QueryClient();

const rootElement = document.getElementById('root');

if (rootElement === null) {
  throw new Error('Root element #root was not found.');
}

createRoot(rootElement).render(
  <StrictMode>
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <GrimoireClientProvider value={grimoireClient}>
          <RouterProvider router={router} />
        </GrimoireClientProvider>
      </QueryClientProvider>
    </I18nextProvider>
  </StrictMode>,
);
