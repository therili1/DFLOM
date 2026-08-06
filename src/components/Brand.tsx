import { Sparkles } from "lucide-react";

export default function Brand() {
  return (
    <div className="brand">
      <div className="brand-mark">
        <img src="/dfl-icon.ico" alt="DFL" />
      </div>
      <div>
        <div className="brand-name">Dream Future</div>
        <div className="brand-subtitle">Launcher</div>
      </div>
      <Sparkles className="brand-sparkle" size={14} />
    </div>
  );
}