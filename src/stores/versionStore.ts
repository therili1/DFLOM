import { create } from "zustand";
import { MinecraftVersionService, type MinecraftVersion } from "../services/version/MinecraftVersionService";

type VersionState = {
  versions: MinecraftVersion[];
  latestRelease: string | null;
  latestSnapshot: string | null;
  loading: boolean;
  error: string | null;
  refreshVersions: () => Promise<void>;
};

export const useVersionStore = create<VersionState>((set) => ({
  versions: [],
  latestRelease: null,
  latestSnapshot: null,
  loading: false,
  error: null,
  refreshVersions: async () => {
    set({ loading: true, error: null });
    try {
      const result = await MinecraftVersionService.fetchVersions();
      set({ ...result, loading: false });
    } catch (error) {
      set({ loading: false, error: error instanceof Error ? error.message : String(error) });
    }
  },
}));