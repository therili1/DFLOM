import { useState } from "react";
import { Check, LoaderCircle, Send, Sparkles } from "lucide-react";
import { ThemeEngineService } from "../services/theme/ThemeEngineService";

export type ChatMessage = { role: "user" | "assistant"; text: string };
export type ChatMode = "develop" | "update";

type Props = {
  hasKey: boolean | null;
  onCssApplied: (path: string) => void;
  /** Called instead of onCssApplied when the reply's fence is labeled for a
   *  specific page (```css:sidebar) -- see HIDDEN_CHAT_INSTRUCTIONS in
   *  lib.rs. Meaningful either in "update" mode (there's an active
   *  installed theme to write pages/<key>.css into directly) or, when
   *  `draftMode` is set, in "develop" mode too (writes a standalone temp
   *  file instead, whose path is passed as the second argument so the
   *  caller can stash it the same way it stashes onCssApplied's path). If
   *  omitted, page-labeled fences fall back to being applied as a plain
   *  custom.css like any other. */
  onPageCssApplied?: (pageKey: string, draftPath?: string) => void;
  /** Set by Theme Maker (packing a NEW, not-yet-installed theme): a
   *  page-targeted apply can't write into an installed theme's pages/
   *  folder because there isn't one yet, so it writes a standalone temp
   *  file instead -- same as an untargeted apply, just kept separate per
   *  page key. Theme Editor (editing an already-installed theme) leaves
   *  this unset so page-targeted applies go straight to disk. */
  draftMode?: boolean;
};

// A ```css or ```css:<page-key> fence anywhere in a reply means there's
// something the user could apply -- shown as a per-message "Apply" action
// rather than auto-applying every reply. Returns the page key when the
// fence is labeled (```css:sidebar -> "sidebar"), or null for a plain
// ```css (or unlabeled ```) fence.
function detectCssFence(text: string): { present: boolean; pageKey: string | null } {
  const labeled = text.match(/```css:([a-z0-9-]+)/i);
  if (labeled) return { present: true, pageKey: labeled[1].toLowerCase() };
  const present = text.includes("```css") || (text.includes("```") && /[.#][a-zA-Z-]/.test(text));
  return { present, pageKey: null };
}

export default function ThemeAiChatPanel({ hasKey, onCssApplied, onPageCssApplied, draftMode }: Props) {
  const [mode, setMode] = useState<ChatMode>("develop");
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [sending, setSending] = useState(false);
  const [applyingIndex, setApplyingIndex] = useState<number | null>(null);
  const [appliedIndex, setAppliedIndex] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Switching mode changes which reference files the AI reads behind the
  // scenes (template vs. the currently active theme) -- starting a new
  // conversation avoids mixing context from two different themes.
  const switchMode = (next: ChatMode) => {
    if (next === mode) return;
    setMode(next);
    setMessages([]);
    setError(null);
  };

  const send = async () => {
    const text = input.trim();
    if (!text || sending) return;
    setError(null);
    const nextMessages: ChatMessage[] = [...messages, { role: "user", text }];
    setMessages(nextMessages);
    setInput("");
    setSending(true);
    try {
      const reply = await ThemeEngineService.chat(messages, text, mode);
      setMessages([...nextMessages, { role: "assistant", text: reply }]);
    } catch (chatError) {
      setError(chatError instanceof Error ? chatError.message : String(chatError));
    } finally {
      setSending(false);
    }
  };

  const applyMessage = async (index: number, text: string, pageKey: string | null) => {
    setError(null);
    setApplyingIndex(index);
    try {
      // Target a page when the caller opted in (onPageCssApplied given) AND
      // either draftMode is set (Theme Maker -- writes a standalone temp
      // file, no active theme required) or we're in "update" mode (Theme
      // Editor -- there's an active installed theme to write
      // pages/<key>.css into directly). Otherwise treat the fence like a
      // normal, untargeted one.
      const targetKey = pageKey && onPageCssApplied && (draftMode || mode === "update") ? pageKey : null;
      const result = await ThemeEngineService.saveChatMessageAsCss(text, targetKey, !!draftMode);
      if (targetKey) {
        onPageCssApplied!(targetKey, draftMode ? result : undefined);
      } else {
        onCssApplied(result);
      }
      setAppliedIndex(index);
    } catch (applyError) {
      setError(applyError instanceof Error ? applyError.message : String(applyError));
    } finally {
      setApplyingIndex(null);
    }
  };

  return <div style={{ display: "flex", flexDirection: "column", gap: 8, padding: 12, border: "1px solid var(--border-color, #333)", borderRadius: 8 }}>
    <strong style={{ fontSize: 12, display: "flex", alignItems: "center", gap: 6 }}><Sparkles size={14} /> AI theme chat</strong>
    {hasKey === false && <p className="data-dir-note">No Google AI Studio API key saved yet — add one in Settings to use this.</p>}

    <div style={{ display: "flex", gap: 4 }}>
      <button
        className={`secondary-button${mode === "develop" ? " active" : ""}`}
        style={{ flex: 1, opacity: mode === "develop" ? 1 : 0.6 }}
        onClick={() => switchMode("develop")}
      >
        Розробка
      </button>
      <button
        className={`secondary-button${mode === "update" ? " active" : ""}`}
        style={{ flex: 1, opacity: mode === "update" ? 1 : 0.6 }}
        onClick={() => switchMode("update")}
      >
        Оновлення теми
      </button>
    </div>
    <p className="data-dir-note">
      {mode === "develop"
        ? "AI будує тему з нуля, орієнтуючись на наш стандартний шаблон .dftp."
        : "AI редагує CSS твоєї поточної активної теми (Theme Editor \u2192 Activate)."}
    </p>

    {messages.length > 0 && <div style={{ display: "flex", flexDirection: "column", gap: 8, maxHeight: 320, overflowY: "auto" }}>
      {messages.map((message, index) => <div
        key={index}
        style={{
          alignSelf: message.role === "user" ? "flex-end" : "flex-start",
          maxWidth: "85%",
          background: message.role === "user" ? "var(--accent)" : "var(--card-bg, #1b2431)",
          color: message.role === "user" ? "#fff" : "inherit",
          borderRadius: 10,
          padding: "8px 12px",
          fontSize: 13,
          whiteSpace: "pre-wrap",
        }}
      >
        {message.text}
        {message.role === "assistant" && (() => {
          const fence = detectCssFence(message.text);
          if (!fence.present) return null;
          const targeting = fence.pageKey && onPageCssApplied && (draftMode || mode === "update");
          return <div style={{ marginTop: 6 }}>
            <button
              className="secondary-button"
              onClick={() => void applyMessage(index, message.text, fence.pageKey)}
              disabled={applyingIndex === index}
              style={{ fontSize: 12, padding: "4px 8px" }}
            >
              {applyingIndex === index ? <LoaderCircle className="spin" size={12} /> : appliedIndex === index ? <Check size={12} /> : <Sparkles size={12} />}
              {appliedIndex === index ? " Applied" : targeting ? ` Apply to pages/${fence.pageKey}.css` : " Apply as custom CSS"}
            </button>
          </div>;
        })()}
      </div>)}
      {sending && <div style={{ alignSelf: "flex-start", fontSize: 13, opacity: 0.7 }}><LoaderCircle className="spin" size={13} /> Thinking...</div>}
    </div>}

    <div style={{ display: "flex", gap: 8 }}>
      <input
        placeholder="e.g. 'make it a neon purple cyberpunk vibe' or 'now make the sidebar glow'"
        value={input}
        onChange={(event) => setInput(event.target.value)}
        onKeyDown={(event) => { if (event.key === "Enter" && !event.shiftKey) { event.preventDefault(); void send(); } }}
        disabled={hasKey !== true}
        style={{ flex: 1 }}
      />
      <button className="secondary-button" onClick={() => void send()} disabled={hasKey !== true || sending || !input.trim()}>
        {sending ? <LoaderCircle className="spin" size={14} /> : <Send size={14} />}
      </button>
    </div>
    {error && <div className="java-error">{error}</div>}
  </div>;
}
