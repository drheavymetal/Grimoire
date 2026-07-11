import { useEffect, useRef, useState } from 'react';

// Measures a container's pixel width and tracks it across resizes, so the timeline layout
// (pure, in core/) can be recomputed for the real width — auto-fit by transforming
// positions in JS, never by scaling an SVG <g> (D18). ResizeObserver is a browser API and
// therefore lives here in ui/, not in core/. Returns a ref to attach and the current width.
export function useMeasuredWidth<T extends HTMLElement>(): [React.RefObject<T | null>, number] {
  const ref = useRef<T | null>(null);
  const [width, setWidth] = useState(0);

  useEffect(() => {
    const element = ref.current;
    if (element === null) {
      return;
    }

    const update = (): void => {
      setWidth(element.clientWidth);
    };

    update();

    let observer: ResizeObserver | null = null;
    if (typeof ResizeObserver !== 'undefined') {
      observer = new ResizeObserver(update);
      observer.observe(element);
    }

    return () => {
      if (observer !== null) {
        observer.disconnect();
      }
    };
  }, []);

  return [ref, width];
}
