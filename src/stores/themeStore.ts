import { create } from "zustand";

// NOTE: this store used to also hold the old standalone CSS-import theme
// system (themes/activeTheme/loadThemes/importTheme/applyTheme/deleteTheme).
// That system was removed and folded into the .dftp Theme Engine (see
// themeEngineStore.ts) — a .dftp can now bundle an optional custom.css,
// applied the same <style>-tag-injection way. This store now only holds
// the built-in dark/light toggle, which is unrelated to theme packs.
type ThemeStore = {
  isDark: boolean;
  toggleTheme: () => void;
};

export const useThemeStore = create<ThemeStore>((set) => ({
  isDark: true,
  toggleTheme: () => set((state) => ({ isDark: !state.isDark })),
}));
