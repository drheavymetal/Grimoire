import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import en from './locales/en.json';
import es from './locales/es.json';
import { webStorage } from './platform/storage.web';

// i18n is initialised from the first commit (invariant 7). Keys are in English.
const STORAGE_KEY = 'grimoire-lang';
const stored = webStorage.get(STORAGE_KEY);
const initialLanguage = stored === 'es' || stored === 'en' ? stored : 'en';

void i18n.use(initReactI18next).init({
  resources: {
    en: { translation: en },
    es: { translation: es },
  },
  lng: initialLanguage,
  fallbackLng: 'en',
  interpolation: {
    escapeValue: false,
  },
});

export function persistLanguage(language: string): void {
  webStorage.set(STORAGE_KEY, language);
}

export default i18n;
