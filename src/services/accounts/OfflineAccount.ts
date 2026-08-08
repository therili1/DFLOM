import { convertFileSrc } from "@tauri-apps/api/core";
import { offlineUuid } from "./UUIDGenerator";
export type Account = { id: string; username: string; uuid: string; type: "Offline" | "Ely.by" | "Microsoft"; createdAt: string; lastPlayed: string | null; skinPath: string; capePath: string; favorite: boolean; email?: string; accessToken?: string; clientToken?: string; refreshToken?: string };
export function createOfflineAccount(username: string): Account {
  const now = new Date().toISOString();
  return { id: crypto.randomUUID(), username, uuid: offlineUuid(username), type: "Offline", createdAt: now, lastPlayed: null, skinPath: `https://mc-heads.net/avatar/${encodeURIComponent(username)}`, capePath: "", favorite: false };
}

// skinPath/capePath are either a remote URL (the default mc-heads.net
// avatar, or anything a user pasted) or a local filesystem path picked via
// browse_skin_file -- those need convertFileSrc to actually load in the
// webview, remote URLs must NOT be passed through it (it only knows how to
// rewrite local paths). Used everywhere an account's skin/cape is
// rendered as an <img>.
export function resolveAvatarSrc(path: string | undefined | null): string | undefined {
  if (!path) return undefined;
  if (/^([a-z][a-z0-9+.-]*:)?\/\//i.test(path) || path.startsWith("data:")) return path;
  return convertFileSrc(path);
}