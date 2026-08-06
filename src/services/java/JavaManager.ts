import { invoke } from "@tauri-apps/api/core";

export type JavaInstallation = {
  path: string;
  version: string;
  vendor: string;
  arch: string;
  runtime: string;
  compatibleVersions?: number[];
  managed?: boolean;
};

export const JavaManager = {
  scan: () => invoke<JavaInstallation[]>("scan_java"),
  save: (path: string) => invoke<JavaInstallation>("save_java", { path }),
  remove: (path: string) => invoke<void>("remove_java", { path }),
  browse: () => invoke<string | null>("browse_java"),
  download: (major: number) => invoke<JavaInstallation>("download_java", { major }),
  openFolder: (path: string) => invoke<void>("open_java_folder", { path }),
  deleteRuntime: (path: string) => invoke<void>("delete_java_runtime", { path }),
};