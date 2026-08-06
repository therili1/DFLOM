import { create } from "zustand";

const STORAGE_KEY = "dfl:accentColor";
export const DEFAULT_ACCENT = "#7f6ae0";

export const ACCENT_PRESETS: { name: string; color: string }[] = [
  { name: "Violet", color: "#7f6ae0" },
  { name: "Blue", color: "#4f8ff7" },
  { name: "Teal", color: "#2fb6a9" },
  { name: "Green", color: "#4caf6f" },
  { name: "Amber", color: "#e0a75e" },
  { name: "Red", color: "#e0596a" },
  { name: "Pink", color: "#e065b8" },
];

function setCssAccent(color: string) {
  document.documentElement.style.setProperty("--accent", color);
}

type AccentStore = {
  accent: string;
  setAccent: (color: string) => void;
  apply: () => void;
};

export const useAccentStore = create<AccentStore>((set) => ({
  accent: DEFAULT_ACCENT,
  setAccent: (color) => {
    setCssAccent(color);
    localStorage.setItem(STORAGE_KEY, color);
    set({ accent: color });
  },
  apply: () => {
    const saved = localStorage.getItem(STORAGE_KEY) || DEFAULT_ACCENT;
    setCssAccent(saved);
    set({ accent: saved });
  },
}));
