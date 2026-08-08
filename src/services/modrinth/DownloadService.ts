import { invoke } from "@tauri-apps/api/core";
import type { ModrinthVersion } from "./VersionService";

function pickFile(version: ModrinthVersion) {
  const file = version.files.find((item) => item.primary) ?? version.files[0];
  if (!file) throw new Error("Modrinth version has no downloadable file.");
  return file;
}

// Modrinth's API returns loader ids lowercase ("fabric", "forge", "quilt",
// "neoforge", ...), but Instances.tsx's launch flow (and the loader-version
// pickers) compare instance.loader against the capitalized display names
// ("Fabric", "Forge", "Quilt", "NeoForge"). Passing the raw lowercase id
// straight through silently missed every one of those checks and fell back
// to the plain-vanilla launch path -- the instance would download vanilla
// Minecraft only, never the mod loader itself, so any mod that needs the
// loader's classes just crashed or failed to load. Normalize here so every
// modpack install always produces one of the exact strings the rest of the
// app recognizes.
const KNOWN_LOADERS: Record<string, string> = {
  fabric: "Fabric",
  quilt: "Quilt",
  forge: "Forge",
  neoforge: "NeoForge",
};

export function normalizeLoaderName(rawLoader: string | undefined | null): string {
  if (!rawLoader) return "Vanilla";
  return KNOWN_LOADERS[rawLoader.toLowerCase()] ?? rawLoader;
}

export const DownloadService = {
  install: (url: string, instanceDirectory: string, projectType: string, filename: string, size: number) =>
    invoke<void>("install_modrinth_file", { url, instanceDirectory, projectType, filename, size }),

  installModpack: (url: string, filename: string, instanceName: string, minecraftVersion: string, loader: string, iconUrl?: string) =>
    invoke<{ name: string }>("install_modrinth_modpack", { url, filename, instanceName, minecraftVersion, loader, iconUrl }),

  // Installs a mod/shader/resourcepack/datapack version into an existing instance.
  installVersion: async (version: ModrinthVersion, instanceDirectory: string, projectType: string) => {
    const file = pickFile(version);
    await DownloadService.install(file.url, instanceDirectory, projectType, file.filename, file.size);
  },

  // Downloads a modpack version and creates a brand new instance from it.
  // iconUrl (the source project's own icon, e.g. project.icon_url from the
  // marketplace search result) is downloaded once server-side and stored
  // locally with the instance -- see download_instance_icon() in lib.rs --
  // so the new instance card shows real artwork instead of the generic icon.
  installModpackVersion: async (version: ModrinthVersion, instanceName: string, iconUrl?: string) => {
    const file = pickFile(version);
    const minecraftVersion = version.game_versions[0] ?? "Unknown";
    const loader = normalizeLoaderName(version.loaders[0]);
    return DownloadService.installModpack(file.url, file.filename, instanceName, minecraftVersion, loader, iconUrl);
  },
};
