import { useEffect, useState } from 'react';

// Debounce any value. Uses setTimeout, which exists on every platform (no DOM).
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const handle = setTimeout(() => {
      setDebounced(value);
    }, delayMs);

    return () => {
      clearTimeout(handle);
    };
  }, [value, delayMs]);

  return debounced;
}
