/** Ladder from the Brawlhalla wiki. Valhallan is placement-based. */
const TIER_FLOORS: ReadonlyArray<readonly [number, string]> = [
  [2000, 'Diamond'],
  [1680, 'Platinum'],
  [1390, 'Gold'],
  [1130, 'Silver'],
  [910, 'Bronze'],
  [0, 'Tin'],
];

/** Longest first: "Platinum" also contains "tin". */
const TIER_KEYS = ['valhallan', 'platinum', 'diamond', 'bronze', 'silver', 'gold', 'tin'] as const;

const REGION_NAMES: Readonly<Record<string, string>> = {
  'US-E': 'US East',
  'US-W': 'US West',
  EU: 'Europe',
  SEA: 'SE Asia',
  BRZ: 'Brazil',
  AUS: 'Australia',
  JPS: 'Japan',
  SA: 'South Africa',
  ME: 'Middle East',
};

export function tierName(tier: string | null | undefined, rating: number | null | undefined): string | null {
  const named = tier?.trim();
  if (named) {
    return named;
  }
  if (rating == null) {
    return null;
  }
  return TIER_FLOORS.find(([floor]) => rating >= floor)?.[1] ?? null;
}

/** Lowercase family used by colours and the Rank chips. */
export function tierKey(tier: string | null | undefined, rating: number | null | undefined): string | null {
  const named = (tier ?? '').trim().toLowerCase();
  const matched = TIER_KEYS.find((key) => named.includes(key));
  if (matched) {
    return matched;
  }
  if (rating == null) {
    return null;
  }
  return TIER_FLOORS.find(([floor]) => rating >= floor)?.[1].toLowerCase() ?? null;
}

export function regionName(region: string | null | undefined): string {
  const code = region?.trim() ?? '';
  return REGION_NAMES[code] ?? code;
}
