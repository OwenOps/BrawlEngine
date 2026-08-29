export interface LoadoutMod {
  modId: number;
}

export interface Loadout {
  maps: LoadoutMod[];
  music: LoadoutMod[];
}

export interface NamedConfig {
  id: string;
  name: string;
  maps: LoadoutMod[];
  music: LoadoutMod[];
}

export interface NamedConfigList {
  configs: NamedConfig[];
}
