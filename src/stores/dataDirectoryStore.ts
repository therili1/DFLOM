import { create } from "zustand";
import { DataDirectoryService } from "../services/data/DataDirectoryService";

type DataDirectoryState = {
  path: string | null;
  loading: boolean;
  error: string | null;
  load: () => Promise<void>;
  browseAndSet: () => Promise<void>;
  reset: () => Promise<void>;
};

export const useDataDirectoryStore = create<DataDirectoryState>((set) => ({
  path: null,
  loading: false,
  error: null,

  load: async () => {
    set({ loading: true, error: null });
    try {
      const path = await DataDirectoryService.get();
      set({ path, loading: false });
    } catch (error) {
      set({ loading: false, error: String(error) });
    }
  },

  browseAndSet: async () => {
    set({ error: null });
    try {
      const chosen = await DataDirectoryService.browse();
      if (!chosen) return; // user cancelled
      const path = await DataDirectoryService.set(chosen);
      set({ path });
    } catch (error) {
      set({ error: String(error) });
    }
  },

  reset: async () => {
    set({ error: null });
    try {
      const path = await DataDirectoryService.set("");
      set({ path });
    } catch (error) {
      set({ error: String(error) });
    }
  },
}));
