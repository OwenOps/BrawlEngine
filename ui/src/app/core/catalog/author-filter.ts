import { CatalogItem } from '../ipc/contracts/catalog.contracts';

export interface AuthorFilter {
  id: number;
  name: string;
}

export function matchesAuthor(item: CatalogItem, author: AuthorFilter | null): boolean {
  if (!author) {
    return true;
  }

  if (author.id > 0 && item.authorId === author.id) {
    return true;
  }

  return author.name.length > 0 && item.author.toLowerCase() === author.name.toLowerCase();
}
