import { invoke } from "@tauri-apps/api/core";

export type ThemeEngineInfo = {
  id: string;
  name: string;
  author: string;
  version: string;
  backgroundPath: string | null;
  previewPath: string | null;
  isActive: boolean;
  hasCustomCss: boolean;
  sidebarPosition: "left" | "right" | "top" | "bottom";
  hiddenTabs: string[];
  tabOrder: string[];
};

export type ThemeDraft = {
  name: string;
  author: string;
  version: string;
  backgroundPath: string | null;
  previewPath: string | null;
  fontPaths: string[];
  customCssPath: string | null;
  sidebarPosition: string;
  hiddenTabs: string[];
  tabOrder: string[];
  /** Hybrid Mode only: page key -> chosen CSS file path (sidebar/topbar/
   *  home/each nav tab). Omitted or empty in Standard Mode. */
  pageCssPaths?: Record<string, string>;
};

export const ThemeEngineService = {
  browse: () => invoke<string | null>("browse_dftp_file"),
  install: (archivePath: string) => invoke<ThemeEngineInfo>("theme_install", { archivePath }),
  list: () => invoke<ThemeEngineInfo[]>("theme_list"),
  current: () => invoke<ThemeEngineInfo | null>("theme_current"),
  activate: (themeId: string) => invoke<void>("theme_activate", { themeId }),
  deactivate: () => invoke<void>("theme_deactivate"),
  remove: (themeId: string) => invoke<void>("theme_remove", { themeId }),
  readCss: (themeId: string) => invoke<string | null>("theme_read_css", { themeId }),
  readPageCss: (themeId: string) => invoke<Record<string, string>>("theme_read_page_css", { themeId }),
  writePageCss: (themeId: string, pageKey: string, css: string) =>
    invoke<void>("theme_write_page_css", { themeId, pageKey, css }),
  updateLayout: (themeId: string, sidebarPosition: string, hiddenTabs: string[], tabOrder: string[]) =>
    invoke<void>("theme_update_layout", { themeId, sidebarPosition, hiddenTabs, tabOrder }),

  // Theme Maker
  browseAsset: () => invoke<string | null>("browse_theme_asset"),
  browseFonts: () => invoke<string[]>("browse_theme_fonts"),
  browseCustomCss: () => invoke<string | null>("browse_custom_css_file"),
  pack: (draft: ThemeDraft) => invoke<string>("theme_pack", draft),
  downloadTemplate: () => invoke<string>("theme_download_template"),
  downloadDevExample: () => invoke<string>("theme_download_dev_example"),
  downloadVideoExample: () => invoke<string>("theme_download_video_example"),
  hasGeminiApiKey: () => invoke<boolean>("has_gemini_api_key"),
  generateCss: (description: string) => invoke<string>("generate_theme_css", { description }),
  chat: (history: { role: "user" | "assistant"; text: string }[], message: string, mode: "develop" | "update") =>
    invoke<string>("gemini_chat", { history, message, mode }),
  saveChatMessageAsCss: (message: string, pageKey?: string | null, draft?: boolean) =>
    invoke<string>("save_chat_message_as_css", { message, pageKey: pageKey ?? null, draft: draft ?? false }),
};
