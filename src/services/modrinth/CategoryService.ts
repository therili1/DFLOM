// Static category / loader taxonomies used to build the filter sidebar on
// the Marketplace page. Modrinth exposes a live `/tag/category` endpoint,
// but its result set is effectively fixed and rarely changes, so we ship
// it statically here to avoid an extra network round-trip every time the
// filter panel opens. CurseForge's category names are also hardcoded --
// there is no `classId`/category-id lookup wired through the Edge
// Function yet, so CurseForge filtering happens client-side by matching
// each mod's own `categories[].name` against the checked labels (see
// Marketplace.tsx `matchesCurseForgeCategories`).

export type MarketSource = "modrinth" | "curseforge";

const MODRINTH_MOD_CATEGORIES = [
  "adventure", "cursed", "decoration", "economy", "equipment", "food",
  "game-mechanics", "library", "magic", "management", "minigame", "mobs",
  "optimization", "social", "storage", "technology", "transportation",
  "utility", "worldgen",
];

const MODRINTH_MODPACK_CATEGORIES = [
  "adventure", "challenging", "combat", "kitchen-sink", "lightweight",
  "magic", "multiplayer", "optimization", "quests", "technology", "vanilla-like",
];

const MODRINTH_RESOURCEPACK_CATEGORIES = [
  "8x-or-lower", "16x", "32x", "48x", "64x", "128x", "256x", "512x-or-higher",
  "audio", "combat", "cursed", "decoration", "modded", "realistic",
  "simplistic", "themed", "tweaks", "utility", "vanilla-like",
];

const MODRINTH_SHADER_CATEGORIES = [
  "atmosphere", "bloom", "cartoon", "cursed", "fantasy", "foliage",
  "high", "low", "medium", "potato", "realistic", "reflections",
  "screenshot", "semi-realistic", "shadows", "vanilla-like",
];

const MODRINTH_DATAPACK_CATEGORIES = [
  "adventure", "economy", "equipment", "food", "game-mechanics", "library",
  "magic", "management", "minigame", "mobs", "optimization", "social",
  "storage", "technology", "utility", "worldgen",
];

// CurseForge's own (approximate, publicly documented) category names --
// deliberately kept close to the real labels shown at curseforge.com so
// the client-side name match in Marketplace.tsx has something sensible
// to compare against.
const CURSEFORGE_MOD_CATEGORIES = [
  "Adventure and RPG", "API and Library", "Armor, Tools, and Weapons",
  "Cosmetic", "Server Utility", "Storage", "Technology", "Magic",
  "Map and Information", "Miscellaneous", "Ores and Resources",
  "Player Transport", "Structures", "World Gen", "Food", "Mobs",
];

const CURSEFORGE_MODPACK_CATEGORIES = [
  "Adventure and RPG", "Combat / PvP", "Exploration", "Extra Large",
  "FTB", "Game Maps", "Hardcore", "Horror", "Magic", "Mini Game",
  "Multiplayer", "Quests", "Sci-Fi", "Skyblock", "Small / Light",
  "Tech", "Vanilla+",
];

// Corresponds to CurseForge's `classId` for Minecraft (gameId 432). These
// two are the well-known, stable ids; other project types aren't wired
// into the CurseForge search command yet.
export const CURSEFORGE_CLASS_ID: Record<string, number | undefined> = {
  mod: 6,
  modpack: 4471,
};

const MODRINTH_CATEGORIES_BY_TYPE: Record<string, string[]> = {
  mod: MODRINTH_MOD_CATEGORIES,
  modpack: MODRINTH_MODPACK_CATEGORIES,
  resourcepack: MODRINTH_RESOURCEPACK_CATEGORIES,
  shader: MODRINTH_SHADER_CATEGORIES,
  datapack: MODRINTH_DATAPACK_CATEGORIES,
};

const CURSEFORGE_CATEGORIES_BY_TYPE: Record<string, string[]> = {
  mod: CURSEFORGE_MOD_CATEGORIES,
  modpack: CURSEFORGE_MODPACK_CATEGORIES,
};

export const CategoryService = {
  types: ["mod", "modpack", "shader", "resourcepack", "datapack"],
  loaders: ["fabric", "forge", "neoforge", "quilt", "paper", "spigot", "bukkit", "vanilla"],

  // Only mods/modpacks currently have a CurseForge counterpart wired up.
  curseforgeCapableTypes: new Set(["mod", "modpack"]),

  categoriesFor(projectType: string, source: MarketSource): string[] {
    if (source === "curseforge") return CURSEFORGE_CATEGORIES_BY_TYPE[projectType] ?? [];
    return MODRINTH_CATEGORIES_BY_TYPE[projectType] ?? [];
  },

  // Loader checkboxes only make sense for Modrinth today -- CurseForge
  // search results returned by the thin CurseForgeMod type don't carry a
  // per-file loader tag, so filtering by loader would silently do nothing.
  loadersAvailable(source: MarketSource): boolean {
    return source === "modrinth";
  },
};
