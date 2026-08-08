import { invoke } from "@tauri-apps/api/core";

export type AssistantChatMessage = { role: "user" | "assistant"; text: string };

// General-help chat for the sidebar "AI Helper" -- distinct backend command
// (assistant_chat) and hidden system prompt from Theme Maker's gemini_chat.
// Shares only the underlying Gemini key/plumbing with ThemeEngineService.
export const AssistantService = {
  isEnabled: () => invoke<boolean>("get_ai_helper_enabled"),
  isVisible: () => invoke<boolean>("get_ai_helper_visible"),
  setEnabled: (enabled: boolean) => invoke<void>("set_ai_helper_enabled", { enabled }),
  chat: (history: AssistantChatMessage[], message: string) =>
    invoke<string>("assistant_chat", { history, message }),
};
