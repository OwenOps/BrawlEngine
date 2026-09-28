export interface StatsRecent {
  id: number;
  name: string;
}

export interface StatsSaved {
  recents: StatsRecent[];
  pins: StatsRecent[];
  mine: StatsRecent | null;
}

export interface PlayerTeammate {
  id: number;
  name: string;
}

export interface PlayerSearchMatch {
  id: number;
  name: string;
  gameMode: string;
  region: string | null;
  tier: string | null;
  rating: number | null;
  peakRating: number | null;
  rank: number | null;
  wins: number | null;
  losses: number | null;
  teammates: PlayerTeammate[] | null;
}

export interface PlayerSearchPage {
  matches: PlayerSearchMatch[];
  query: string;
  gameMode: string;
  totalPages: number;
  warning?: string | null;
  logPath?: string | null;
}

export interface PlayerRanked {
  gameMode: string;
  region: string | null;
  tier: string | null;
  rating: number | null;
  peakRating: number | null;
  rank: number | null;
  wins: number | null;
  losses: number | null;
  teammates: PlayerTeammate[] | null;
}

export interface PlayerLegend {
  id: number;
  name: string;
  games: number;
  wins: number;
  level: number | null;
}

export interface PlayerProfile {
  id: number;
  name: string;
  level: number | null;
  games: number | null;
  wins: number | null;
  guildName: string | null;
  ranked: PlayerRanked[];
  legends: PlayerLegend[];
  playSeconds: number | null;
}
