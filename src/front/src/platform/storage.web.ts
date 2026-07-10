// Web storage adapter. The native build supplies storage.native.ts (AsyncStorage).
export interface KeyValueStorage {
  get(key: string): string | null;
  set(key: string, value: string): void;
}

export const webStorage: KeyValueStorage = {
  get(key) {
    try {
      return window.localStorage.getItem(key);
    } catch {
      return null;
    }
  },
  set(key, value) {
    try {
      window.localStorage.setItem(key, value);
    } catch {
      // Storage can be unavailable (private mode); fail silently.
    }
  },
};
