export type LibraryFilter = 'all' | 'disk' | 'applied';

export const LIBRARY_FILTERS: ReadonlyArray<{ id: LibraryFilter; label: string }> = [
  { id: 'all', label: 'All' },
  { id: 'disk', label: 'On disk' },
  { id: 'applied', label: 'Applied' },
];
