import { webStorage } from './storage.web';

// Web theme adapter: reads/writes the persisted theme and reflects it on <html>.
// This is the only place allowed to touch document for theming; core never does.
export type Theme = 'dark' | 'light';

const STORAGE_KEY = 'grimoire-theme';

export function readTheme(): Theme {
  const stored = webStorage.get(STORAGE_KEY);

  if (stored === 'light' || stored === 'dark') {
    return stored;
  }

  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
}

export function applyTheme(theme: Theme): void {
  document.documentElement.classList.toggle('dark', theme === 'dark');
  document.documentElement.dataset.theme = theme;
  webStorage.set(STORAGE_KEY, theme);
}
