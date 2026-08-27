export interface PhotinoExternal {
  sendMessage(message: string): void;
  receiveMessage(callback: (message: string) => void): void;
}

export function photinoExternal(): PhotinoExternal | undefined {
  const ext = (window as unknown as { external?: PhotinoExternal }).external;
  if (ext && typeof ext.sendMessage === 'function') {
    return ext;
  }
  return undefined;
}
