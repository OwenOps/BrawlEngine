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
  nextApiPage: number;
  complete: boolean;
}
