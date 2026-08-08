import { create } from "zustand";
import { AssistantService } from "../services/ai/AssistantService";

type AiHelperStore = {
  // True only when the Settings toggle is on AND a Gemini key is saved --
  // both conditions checked together server-side (get_ai_helper_visible).
  visible: boolean;
  refresh: () => Promise<void>;
};

export const useAiHelperStore = create<AiHelperStore>((set) => ({
  visible: false,
  // Called on app mount and again by Settings.tsx any time the toggle or
  // the Gemini key changes, so the sidebar item appears/disappears
  // immediately without needing a restart.
  refresh: async () => {
    try {
      set({ visible: await AssistantService.isVisible() });
    } catch {
      set({ visible: false });
    }
  },
}));
