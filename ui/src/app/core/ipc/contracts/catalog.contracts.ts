export interface CatalogItem {
  id: number;
  name: string;
  author: string;
  thumbnailUrl: string | null;
  category: string;
  profileUrl: string;
}

export interface CatalogPage {
  items: CatalogItem[];
  page: number;
  nextApiPage: number;
  complete: boolean;
  totalCount: number;
  pageSize: number;
}

export type CatalogSort = 'newest' | 'liked' | 'downloaded';

export const CATALOG_SORTS: ReadonlyArray<{ id: CatalogSort; label: string }> = [
  { id: 'newest', label: 'Newest' },
  { id: 'liked', label: 'Most liked' },
  { id: 'downloaded', label: 'Most downloaded' },
];
