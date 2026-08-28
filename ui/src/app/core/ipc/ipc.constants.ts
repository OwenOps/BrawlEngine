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
} as const;

export type IpcMessageType = typeof IPC_MESSAGE[keyof typeof IPC_MESSAGE];