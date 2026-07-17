import { webStorage } from './storage.web';
import {
  parseExploreSections,
  serialiseExploreSections,
  type ExploreSectionState,
} from '../core/domain/exploreSections';

// Web adapter for the Explore hub's folded/unfolded state. Same shape as theme.web.ts: this module
// owns the key and the storage call, the pure parser in core/domain owns the shape. Going through
// webStorage rather than localStorage is what keeps core/ DOM-free (invariant 6) and the native port
// cheap — storage.native.ts swaps underneath without either side noticing.
const STORAGE_KEY = 'grimoire-explore-sections';

export function readExploreSections(): ExploreSectionState {
  return parseExploreSections(webStorage.get(STORAGE_KEY));
}

export function writeExploreSections(state: ExploreSectionState): void {
  webStorage.set(STORAGE_KEY, serialiseExploreSections(state));
}
