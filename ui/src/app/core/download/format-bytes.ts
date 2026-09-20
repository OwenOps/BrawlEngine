/** Human-readable folder size for delete / library UI. */
export function formatBytes(bytes: number): string {
  if (bytes <= 0) {
    return '0 B';
  }
  if (bytes < 1024) {
    return bytes + ' B';
  }
  if (bytes < 1024 * 1024) {
    return Math.round(bytes / 1024) + ' KB';
  }
  if (bytes < 1024 * 1024 * 1024) {
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  }
  return (bytes / (1024 * 1024 * 1024)).toFixed(2) + ' GB';
}

export function confirmDeleteDownload(
  name: string,
  bytes: number | undefined,
  applied: boolean,
): { proceed: boolean; alsoReset: boolean } {
  const size = bytes !== undefined && bytes > 0 ? ' (' + formatBytes(bytes) + ')' : '';
  const proceed = window.confirm(
    'Remove downloaded files for "' + name + '"' + size + '? This frees disk space.',
  );
  if (!proceed) {
    return { proceed: false, alsoReset: false };
  }

  if (!applied) {
    return { proceed: true, alsoReset: false };
  }

  const alsoReset = window.confirm(
    '"' +
      name +
      '" is applied. Also reset it in the game? Vanilla files come back for this mod. Other applied mods stay.',
  );
  return { proceed: true, alsoReset };
}
