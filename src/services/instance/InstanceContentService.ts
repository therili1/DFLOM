import { invoke } from "@tauri-apps/api/core";

export type InstanceContentSummary = {
  mods: string[];
  resourcepacks: string[];
  shaderpacks: string[];
  worlds: string[];
  screenshots: string[];
};

export type WorldEntry = { instanceName: string; worldName: string; path: string };
export type ScreenshotEntry = { instanceName: string; filename: string; path: string };

export const InstanceContentService = {
  get: (name: string) => invoke<InstanceContentSummary>("get_instance_content", { name }),
  removeFile: (name: string, category: "mod" | "resourcepack" | "shader", filename: string) =>
    invoke<void>("remove_instance_file", { name, category, filename }),
  addFile: (name: string, category: "mod" | "resourcepack" | "shader", sourcePath: string) =>
    invoke<void>("add_instance_file", { name, category, sourcePath }),
  browseLocalFile: () => invoke<string | null>("browse_local_content_file"),

  listAllWorlds: () => invoke<WorldEntry[]>("list_all_worlds"),
  browseDatapack: () => invoke<string | null>("browse_datapack_file"),
  installWorldDatapack: (instanceName: string, worldName: string, sourcePath: string) =>
    invoke<void>("install_world_datapack", { instanceName, worldName, sourcePath }),

  listAllScreenshots: () => invoke<ScreenshotEntry[]>("list_all_screenshots"),
};
