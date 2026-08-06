import { invoke } from "@tauri-apps/api/core";

export const DataDirectoryService = {
  // Current effective data directory (custom override if set, otherwise
  // the OS AppData/Local default).
  get: () => invoke<string>("get_data_directory"),
  // Opens a native folder picker; resolves to null if the user cancels.
  browse: () => invoke<string | null>("browse_data_directory"),
  // Pass an empty string to reset back to the AppData/Local default.
  // Returns the new effective path.
  set: (path: string) => invoke<string>("set_data_directory", { path }),
};
