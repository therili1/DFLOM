import { useState } from "react";
import { Check, LoaderCircle, Send, Sparkles } from "lucide-react";
import { ThemeEngineService } from "../services/theme/ThemeEngineService";

export type ChatMessage = { role: "user" | "assistant"; text: string };
export type ChatMode = "develop" | "update";

type Props = {
  hasKey: boolean | null;
  onCssApplied: (path: string) => void;
};

// A ```css fence anywhere in a reply means there's something the user
// could apply as their theme's custom.css -- shown as a per-message
// "Apply as custom CSS" action rather than auto-applying every reply.
function containsCssFence(text: string): boolean {
  return text.includes("```css") || (text.includes("```") && /[.#][a-zA-Z-]/.test(text));
}

export default function ThemeAiChatPanel({ hasKey, onCssApplied }: Props) {
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

  const applyMessage = async (index: number, text: string) => {
    setError(null);
    setApplyingIndex(index);
    try {
      const path = await ThemeEngineService.saveChatMessageAsCss(text);
      onCssApplied(path);
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
        {message.role === "assistant" && containsCssFence(message.text) && (
          <div style={{ marginTop: 6 }}>
            <button
              className="secondary-button"
              onClick={() => void applyMessage(index, message.text)}
              disabled={applyingIndex === index}
              style={{ fontSize: 12, padding: "4px 8px" }}
            >
              {applyingIndex === index ? <LoaderCircle className="spin" size={12} /> : appliedIndex === index ? <Check size={12} /> : <Sparkles size={12} />}
              {appliedIndex === index ? " Applied" : " Apply as custom CSS"}
            </button>
          </div>
        )}
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
