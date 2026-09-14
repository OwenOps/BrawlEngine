export type LibraryFilter = 'all' | 'disk' | 'applied' | 'notApplied' | 'liked';

export const LIBRARY_FILTERS: ReadonlyArray<{ id: LibraryFilter; label: string }> = [
  { id: 'disk', label: 'On disk' },
  { id: 'applied', label: 'Applied' },
  { id: 'notApplied', label: 'Not applied' },
  { id: 'liked', label: 'Liked' },
  { id: 'all', label: 'All' },
];

export function libraryStatusLabel(filter: LibraryFilter): string {
  if (filter === 'disk') {
    return 'on disk';
  }
  if (filter === 'applied') {
    return 'applied';
  }
  if (filter === 'notApplied') {
    return 'not applied';
  }
  if (filter === 'liked') {
    return 'liked';
  }
  return '';
}

/** Ids for a library chip. All catalog does not call this. */
export function libraryIds(
  filter: LibraryFilter,
  downloaded: Iterable<number>,
  applied: ReadonlySet<number>,
  liked: Iterable<number> = [],
): number[] {
  if (filter === 'applied') {
    return [...applied];
  }

  if (filter === 'liked') {
    return [...liked];
  }

  const onDisk = [...downloaded];
  if (filter === 'notApplied') {
    return onDisk.filter((id) => !applied.has(id));
  }

  return onDisk;
}

export function libraryFollowsLoadout(filter: LibraryFilter): boolean {
  return filter === 'applied' || filter === 'notApplied';
}

/** Liked cards first; keep relative order inside liked and the rest. */
export function pinLikedFirst<T extends { id: number }>(
  items: T[],
  liked: ReadonlySet<number>,
): T[] {
  return pinLibraryCards(items, liked, () => false);
}

/** New downloads first, then liked, then the rest. Same order inside each group. */
export function pinLibraryCards<T extends { id: number }>(
  items: T[],
  liked: ReadonlySet<number>,
  isNew: (id: number) => boolean,
): T[] {
  const fresh: T[] = [];
  const hearts: T[] = [];
  const rest: T[] = [];
  for (const item of items) {
    if (isNew(item.id)) {
      fresh.push(item);
    } else if (liked.has(item.id)) {
      hearts.push(item);
    } else {
      rest.push(item);
    }
  }

  if (fresh.length === 0 && hearts.length === 0) {
    return items;
  }

  return [...fresh, ...hearts, ...rest];
}

/** Catalog All page remembered in the component. Hidden when already on that page. */
export function showBackToPage(lastPage: number, currentPage: number, isLibrary: boolean): boolean {
  return lastPage > 1 && (isLibrary || currentPage !== lastPage);
}

/** Pager jump box. Null means the field is not a page number. */
export function parseCatalogPage(raw: string, totalPages: number): number | null {
  const next = Math.trunc(Number(raw));
  if (!Number.isFinite(next) || totalPages < 1) {
    return null;
  }

  return Math.min(totalPages, Math.max(1, next));
}
