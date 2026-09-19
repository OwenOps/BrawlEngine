export function formatCount(value: number | null | undefined): string {
  if (value === null || value === undefined || value <= 0) {
    return '0';
  }

  if (value >= 10_000) {
    return Math.round(value / 1000) + 'k';
  }

  if (value >= 1000) {
    return (value / 1000).toFixed(1).replace(/\.0$/, '') + 'k';
  }

  return String(value);
}
