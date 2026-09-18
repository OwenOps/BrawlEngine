export interface CatalogItem {
  id: number;
  name: string;
  author: string;
  thumbnailUrl: string | null;
  category: string;
  profileUrl: string;
  /** Store-skin name, or "Default", when GameBanana said so. Omitted when unknown. */
  skinTarget?: string | null;
  /** Short GameBanana description, when it is not the same as the title. */
  description?: string | null;
  /** NSFW when GameBanana says so, or the title/description looks NSFW. Hidden on All until Show NSFW is on. */
  nsfw?: boolean;
  /** GameBanana member page for the submitter, when known. */
  authorUrl?: string | null;
  authorId?: number | null;
  authorAvatarUrl?: string | null;
  likeCount?: number;
  downloadCount?: number;
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
