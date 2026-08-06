import { invoke } from "@tauri-apps/api/core";
import type { ThemeEngineInfo } from "../theme/ThemeEngineService";

export type MarketplaceTheme = {
  id: string;
  name: string;
  author: string;
  description: string;
  preview: string | null;
  downloads: number;
  rating: number;
  ratingCount: number;
  createdAt: string | null;
};

// Rust returns snake_case-ish aliased fields folded into this shape already
// via serde aliases on the Rust side -- but the struct itself is plain
// field names (id, name, author, description, preview, downloads, rating,
// rating_count, created_at), so only rating_count/created_at need mapping.
type RawMarketplaceTheme = Omit<MarketplaceTheme, "ratingCount" | "createdAt"> & {
  rating_count: number;
  created_at: string | null;
};

export type MarketplaceSort = "popular" | "new" | "rating";

export type MarketplaceConnectionStatus = { connected: boolean; message: string };

export const ThemeMarketplaceService = {
  // Marketplace connects automatically using the launcher's built-in
  // Supabase config -- there is no user-facing key to manage.
  checkConnection: () => invoke<MarketplaceConnectionStatus>("marketplace_status"),

  listThemes: async (query: string, sort: MarketplaceSort, page: number): Promise<MarketplaceTheme[]> => {
    const raw = await invoke<RawMarketplaceTheme[]>("marketplace_list_themes", { query, sort, page });
    return raw.map((theme) => ({
      ...theme,
      ratingCount: theme.rating_count,
      createdAt: theme.created_at,
    }));
  },

  install: (themeId: string) => invoke<ThemeEngineInfo>("marketplace_download_theme", { themeId }),
  rate: (themeId: string, rating: number) => invoke<void>("marketplace_rate_theme", { themeId, rating }),
};
