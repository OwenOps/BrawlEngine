import { CatalogItem } from '../ipc/contracts/catalog.contracts';

export interface ApplySelectedRow {
  id: number;
  name: string;
  category: string;
  crash: boolean;
  canApply: boolean;
  skipReason: string | null;
}

export function applySelectedRows(
  ids: number[],
  items: CatalogItem[],
  crashIds: ReadonlySet<number>,
  canApply: (item: CatalogItem | undefined) => { ok: boolean; reason: string | null } = () => ({
    ok: true,
    reason: null,
  }),
): ApplySelectedRow[] {
  const byId = new Map(items.map((item) => [item.id, item]));
  return ids.map((id) => {
    const item = byId.get(id);
    const apply = canApply(item);
    return {
      id,
      name: item?.name ?? `Mod ${id}`,
      category: item?.category ?? '',
      crash: crashIds.has(id),
      canApply: apply.ok,
      skipReason: apply.ok ? null : apply.reason,
    };
  });
}

export function defaultApplySelected(rows: ApplySelectedRow[]): Set<number> {
  return new Set(rows.filter((row) => row.canApply && !row.crash).map((row) => row.id));
}

export function applySelectedDone(okCount: number, failedNames: string[]): string {
  if (failedNames.length === 0) {
    return `Applied ${okCount}.`;
  }

  return `Applied ${okCount}. Failed ${failedNames.length}: ${failedNames.join(', ')}`;
}
