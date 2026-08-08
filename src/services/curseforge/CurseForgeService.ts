import { invoke } from "@tauri-apps/api/core";

// Thin wrapper around the Rust `curseforge` module (src-tauri/src/curseforge.rs).
// The launcher never touches the CurseForge API key directly -- every call
// here goes: this file -> Tauri command -> Supabase Edge Function
// (dfl-curseforge-api) -> Supabase Vault -> CurseForge API. Responses are
// cached server-side in the `curseforge_cache` table for ~30 minutes.

export type CurseForgeMod = {
  id: number;
  name: string;
  summary: string;
  authors: Array<{ name: string }>;
  categories: Array<{ name: string }>;
  logo?: { thumbnailUrl?: string; url?: string };
  downloadCount: number;
};

export type CurseForgeSearchResponse = {
  data: CurseForgeMod[];
};

export type CurseForgeFile = {
  id: number;
  fileName: string;
  gameVersions: string[];
  downloadUrl: string;
  releaseType: number;
  changelog?: string;
  fileLength?: number;
};

// What install_curseforge_modpack (src-tauri/src/curseforge.rs) resolves
// to: the created, launchable instance, plus any mods the manifest
// referenced that couldn't be fetched (e.g. distribution disabled by the
// author on CurseForge). A non-empty `warnings` array is NOT a failure --
// the instance already exists and is ready to launch.
export type Instance = {
  name: string;
  minecraftVersion: string;
  loader: string;
  loaderVersion?: string | null;
  created: string;
  size: number;
  gameDirectory?: string | null;
};

export type CurseForgeModpackInstallResult = {
  instance: Instance;
  warnings: string[];
};

export const CurseForgeService = {
  // classId narrows the search to a CurseForge project class -- 6 = Mods,
  // 4471 = Modpacks (see CategoryService.CURSEFORGE_CLASS_ID). Omit it to
  // search across every class, same as before.
  search: (query: string, classId?: number) =>
    invoke<CurseForgeSearchResponse>("search_curseforge_mods", { query, classId: classId ?? null }),

  getMod: (modId: number | string) =>
    invoke<{ data: CurseForgeMod }>("get_curseforge_mod", { modId: String(modId) }),

  getFiles: (modId: number | string) =>
    invoke<{ data: CurseForgeFile[] }>("get_curseforge_mod_files", { modId: String(modId) }),

  // Full modpack import pipeline: download the pack zip -> read
  // manifest.json -> resolve + download every referenced mod ->
  // copy overrides (configs/resourcepacks/etc.) -> create the instance.
  // Reuses the same "download-progress" event the Modrinth install flow
  // listens to, so Marketplace.tsx's existing progress bar just works.
  installModpack: (downloadUrl: string, fileName: string, instanceName: string, iconUrl?: string) =>
    invoke<CurseForgeModpackInstallResult>("install_curseforge_modpack", { downloadUrl, fileName, instanceName, iconUrl }),
};
