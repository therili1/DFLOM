import { useState } from "react";
import { LoaderCircle, Send, Sparkles } from "lucide-react";
import { AssistantService, type AssistantChatMessage } from "../services/ai/AssistantService";

export default function AiHelper() {
  const [messages, setMessages] = useState<AssistantChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const send = async () => {
    const text = input.trim();
    if (!text || sending) return;
    setError(null);
    const nextMessages: AssistantChatMessage[] = [...messages, { role: "user", text }];
    setMessages(nextMessages);
    setInput("");
    setSending(true);
    try {
      const reply = await AssistantService.chat(messages, text);
      setMessages([...nextMessages, { role: "assistant", text: reply }]);
    } catch (chatError) {
      setError(chatError instanceof Error ? chatError.message : String(chatError));
    } finally {
      setSending(false);
    }
  };

  return <div className="ai-helper-page">
    <div className="page-title"><div><span className="eyebrow"><Sparkles size={13} /> AI HELPER</span><h1>AI Helper</h1><p>Запитай про інстанси, моди, Java, акаунти чи будь-що інше в лаунчері.</p></div></div>

    <div className="ai-helper-chat">
      <div className="ai-helper-messages">
        {messages.length === 0 && !sending && <div className="ai-helper-empty">
          <Sparkles size={22} />
          <p>Напиши питання — наприклад, "чому інстанс не запускається" або "яку версію Java обрати для 1.21".</p>
        </div>}
        {messages.map((message, index) => <div key={index} className={`ai-helper-bubble ${message.role}`}>{message.text}</div>)}
        {sending && <div className="ai-helper-bubble assistant thinking"><LoaderCircle className="spin" size={13} /> Думаю…</div>}
      </div>

      <div className="ai-helper-input-row">
        <input
          placeholder="Напиши своє питання…"
          value={input}
          onChange={(event) => setInput(event.target.value)}
          onKeyDown={(event) => { if (event.key === "Enter" && !event.shiftKey) { event.preventDefault(); void send(); } }}
        />
        <button className="primary-button" onClick={() => void send()} disabled={sending || !input.trim()}>
          {sending ? <LoaderCircle className="spin" size={14} /> : <Send size={14} />} Надіслати
        </button>
      </div>
      {error && <div className="java-error">{error}</div>}
    </div>
  </div>;
}
