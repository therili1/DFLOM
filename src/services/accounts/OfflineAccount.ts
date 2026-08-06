import { offlineUuid } from "./UUIDGenerator";
export type Account = { id: string; username: string; uuid: string; type: "Offline" | "Ely.by" | "Microsoft"; createdAt: string; lastPlayed: string | null; skinPath: string; capePath: string; favorite: boolean; email?: string; accessToken?: string; clientToken?: string; refreshToken?: string };
export function createOfflineAccount(username: string): Account {
  const now = new Date().toISOString();
  return { id: crypto.randomUUID(), username, uuid: offlineUuid(username), type: "Offline", createdAt: now, lastPlayed: null, skinPath: `https://mc-heads.net/avatar/${encodeURIComponent(username)}`, capePath: "", favorite: false };
}