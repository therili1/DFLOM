import type { MarketplaceTheme } from "./ThemeMarketplaceService";

// Same idea as services/modrinth/CacheService.ts, but with an explicit TTL:
// the marketplace list shouldn't be re-fetched on every single launch, but
// it should still refresh periodically (and always on an explicit reload)
// so downloads/ratings don't go stale for long.
const PREFIX = "dfl:theme-marketplace:";
const TTL_MS = 10 * 60 * 1000; // 10 minutes

type CacheEntry = { fetchedAt: number; themes: MarketplaceTheme[] };

export const ThemeMarketplaceCache = {
  get(key: string): MarketplaceTheme[] | null {
    try {
      const raw = localStorage.getItem(PREFIX + key);
      if (!raw) return null;
      const entry = JSON.parse(raw) as CacheEntry;
      if (Date.now() - entry.fetchedAt > TTL_MS) return null;
      return entry.themes;
    } catch {
      return null;
    }
  },
  set(key: string, themes: MarketplaceTheme[]) {
    try {
      localStorage.setItem(PREFIX + key, JSON.stringify({ fetchedAt: Date.now(), themes } satisfies CacheEntry));
    } catch {
      // Storage full/unavailable -- caching is a nice-to-have, not required.
    }
  },
};
