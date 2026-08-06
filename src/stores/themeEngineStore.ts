import { create } from "zustand";
import { ThemeEngineService, type ThemeEngineInfo } from "../services/theme/ThemeEngineService";

const STYLE_TAG_ID = "dfl-dftp-custom-css";
const PAGE_STYLE_TAG_ID = "dfl-dftp-page-css";

function injectCss(css: string) {
  let tag = document.getElementById(STYLE_TAG_ID) as HTMLStyleElement | null;
  if (!tag) {
    tag = document.createElement("style");
    tag.id = STYLE_TAG_ID;
    document.head.appendChild(tag);
  }
  tag.textContent = css;
}

function clearCss() {
  document.getElementById(STYLE_TAG_ID)?.remove();
}

// "Hybrid CSS" mode -- separate <style> tag from custom.css's, so it can be
// swapped out on every route change without touching (or re-fetching)
// custom.css. Combines, in order: sidebar.css + topbar.css (always-on
// chrome) + pages/<currentPage>.css (only while that page is open). Any
// of the three can be missing -- a theme with no pages/ folder at all
// just never has anything to put in this tag.
function injectPageCss(css: string) {
  let tag = document.getElementById(PAGE_STYLE_TAG_ID) as HTMLStyleElement | null;
  if (!tag) {
    tag = document.createElement("style");
    tag.id = PAGE_STYLE_TAG_ID;
    document.head.appendChild(tag);
  }
  tag.textContent = css;
}

function clearPageCss() {
  document.getElementById(PAGE_STYLE_TAG_ID)?.remove();
}

type ThemeEngineState = {
  themes: ThemeEngineInfo[];
  current: ThemeEngineInfo | null;
  loading: boolean;
  installing: boolean;
  error: string | null;
  // Raw contents of the active theme's pages/*.css files, keyed by
  // filename without extension ("home", "sidebar", "topbar", ...).
  // Empty object for a theme with no pages/ folder (the common case).
  pageCssMap: Record<string, string>;

  loadThemes: () => Promise<void>;
  loadCurrent: () => Promise<void>;
  installTheme: (path?: string) => Promise<void>;
  activateTheme: (themeId: string) => Promise<void>;
  deactivateTheme: () => Promise<void>;
  removeTheme: (themeId: string) => Promise<void>;
  updateLayout: (themeId: string, sidebarPosition: string, hiddenTabs: string[], tabOrder: string[]) => Promise<void>;
  // Called by MainLayout on every route change. Combines pageCssMap's
  // sidebar.css + topbar.css + pages/<pageKey>.css into the page-css
  // <style> tag. Safe to call even if pageCssMap is empty (clears the tag).
  applyPageCss: (pageKey: string) => void;
};

// Applies (or clears) the active theme's bundled custom.css AND fetches
// its pages/*.css map into the store (applyPageCss then combines that map
// with whatever page is currently open). Called after every action that
// could change which theme is active.
async function syncCustomCss(theme: ThemeEngineInfo | null, setPageCssMap: (map: Record<string, string>) => void) {
  if (!theme || !theme.hasCustomCss) {
    clearCss();
  } else {
    try {
      const css = await ThemeEngineService.readCss(theme.id);
      if (css) injectCss(css); else clearCss();
    } catch {
      clearCss();
    }
  }
  if (!theme) {
    setPageCssMap({});
    clearPageCss();
    return;
  }
  try {
    const pageCssMap = await ThemeEngineService.readPageCss(theme.id);
    setPageCssMap(pageCssMap);
  } catch {
    setPageCssMap({});
  }
}

export const useThemeEngineStore = create<ThemeEngineState>((set, get) => ({
  themes: [],
  current: null,
  loading: false,
  installing: false,
  error: null,
  pageCssMap: {},

  loadThemes: async () => {
    set({ loading: true, error: null });
    try {
      const themes = await ThemeEngineService.list();
      set({ themes, loading: false });
    } catch (error) {
      set({ loading: false, error: error instanceof Error ? error.message : String(error) });
    }
  },

  loadCurrent: async () => {
    try {
      const current = await ThemeEngineService.current();
      set({ current });
      await syncCustomCss(current, (pageCssMap) => set({ pageCssMap }));
    } catch (error) {
      set({ error: error instanceof Error ? error.message : String(error) });
    }
  },

  installTheme: async (path) => {
    set({ error: null });
    try {
      const filePath = path ?? (await ThemeEngineService.browse());
      if (!filePath) return; // user cancelled the file picker
      set({ installing: true });
      const theme = await ThemeEngineService.install(filePath);
      set((state) => ({ themes: [...state.themes.filter((t) => t.id !== theme.id), theme], installing: false }));
    } catch (error) {
      set({ installing: false, error: error instanceof Error ? error.message : String(error) });
    }
  },

  activateTheme: async (themeId) => {
    set({ error: null });
    try {
      await ThemeEngineService.activate(themeId);
      set((state) => ({ themes: state.themes.map((t) => ({ ...t, isActive: t.id === themeId })) }));
      await get().loadCurrent();
    } catch (error) {
      set({ error: error instanceof Error ? error.message : String(error) });
    }
  },

  deactivateTheme: async () => {
    set({ error: null });
    try {
      await ThemeEngineService.deactivate();
      set((state) => ({ themes: state.themes.map((t) => ({ ...t, isActive: false })), current: null }));
      await syncCustomCss(null, (pageCssMap) => set({ pageCssMap }));
    } catch (error) {
      set({ error: error instanceof Error ? error.message : String(error) });
    }
  },

  removeTheme: async (themeId) => {
    set({ error: null });
    try {
      await ThemeEngineService.remove(themeId);
      set((state) => ({ themes: state.themes.filter((t) => t.id !== themeId) }));
      if (get().current?.id === themeId) {
        set({ current: null });
        await syncCustomCss(null, (pageCssMap) => set({ pageCssMap }));
      }
    } catch (error) {
      set({ error: error instanceof Error ? error.message : String(error) });
    }
  },

  updateLayout: async (themeId, sidebarPosition, hiddenTabs, tabOrder) => {
    set({ error: null });
    try {
      await ThemeEngineService.updateLayout(themeId, sidebarPosition, hiddenTabs, tabOrder);
      set((state) => ({
        themes: state.themes.map((t) => (t.id === themeId ? { ...t, sidebarPosition: sidebarPosition as ThemeEngineInfo["sidebarPosition"], hiddenTabs, tabOrder } : t)),
      }));
      await get().loadCurrent();
    } catch (error) {
      set({ error: error instanceof Error ? error.message : String(error) });
    }
  },

  applyPageCss: (pageKey) => {
    const { pageCssMap } = get();
    const parts = [pageCssMap.sidebar, pageCssMap.topbar, pageCssMap[pageKey]].filter((part): part is string => Boolean(part));
    if (parts.length === 0) {
      clearPageCss();
      return;
    }
    injectPageCss(parts.join("\n\n"));
  },
}));
