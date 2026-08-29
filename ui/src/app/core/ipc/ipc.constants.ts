export const IPC_MESSAGE = {
  PING: 'ping',
  GAME_GET: 'game.get',
  GAME_PICK: 'game.pick',
  GAME_RUNNING: 'game.running',
  CATALOG_MAPS: 'catalog.maps',
  MOD_DOWNLOAD: 'mod.download',
  MOD_APPLY: 'mod.apply',
  LOADOUT_GET: 'loadout.get',
  MODS_RESET_ALL: 'mods.resetAll',
  MODS_REAPPLY: 'mods.reapply',
  CATALOG_SOUNDS: 'catalog.sounds',
  SOUND_DOWNLOAD: 'sound.download',
  MUSIC_APPLY: 'music.apply',
  MUSIC_TRACKS: 'music.tracks',
  MUSIC_REPLACE: 'music.replace',
  MODS_RANKED_SAFE: 'mods.rankedSafe',
  CONFIGS_LIST: 'configs.list',
  CONFIGS_SAVE: 'configs.save',
  CONFIGS_LOAD: 'configs.load',
  CONFIGS_DELETE: 'configs.delete',
} as const;

export type IpcMessageType = typeof IPC_MESSAGE[keyof typeof IPC_MESSAGE];