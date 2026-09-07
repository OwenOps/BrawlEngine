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
  return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
}

export function confirmDeleteDownload(name: string, bytes: number | undefined): boolean {
  const size = bytes !== undefined && bytes > 0 ? ' (' + formatBytes(bytes) + ')' : '';
  return window.confirm(
    'Remove downloaded files for "' +
      name +
      '"' +
      size +
      '? This frees disk space. Files already copied into the game stay.',
  );
}
