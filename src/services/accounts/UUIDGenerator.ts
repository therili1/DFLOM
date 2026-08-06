export function offlineUuid(username: string): string {
  const bytes = new TextEncoder().encode(`OfflinePlayer:${username}`);
  let hash = 2166136261;
  for (const byte of bytes) {
    hash ^= byte;
    hash = Math.imul(hash, 16777619);
  }
  const parts = [hash >>> 0, (hash * 31) >>> 0, (hash * 131) >>> 0, (hash * 521) >>> 0];
  const hex = parts.map((part) => part.toString(16).padStart(8, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}