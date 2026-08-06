import { create } from "zustand";

export type LogLine = { stream: "stdout" | "stderr" | "launcher"; line: string; ts: number };
export type LogSession = {
  pid: number;
  instanceName: string;
  version: string;
  status: "running" | "exited";
  exitCode?: number;
  lines: LogLine[];
  startedAt: number;
};

const MAX_LINES_PER_SESSION = 4000;

type LogsStore = {
  sessions: Record<number, LogSession>;
  activePid: number | null;
  setActivePid: (pid: number | null) => void;
  startSession: (pid: number, instanceName: string, version: string) => void;
  appendLine: (pid: number, stream: "stdout" | "stderr" | "launcher", line: string) => void;
  endSession: (pid: number, code: number | null) => void;
  clearSession: (pid: number) => void;
};

export const useLogsStore = create<LogsStore>((set, get) => ({
  sessions: {},
  activePid: null,
  setActivePid: (pid) => set({ activePid: pid }),

  startSession: (pid, instanceName, version) => {
    set((state) => ({
      sessions: { ...state.sessions, [pid]: { pid, instanceName, version, status: "running", lines: [], startedAt: Date.now() } },
      activePid: pid,
    }));
  },

  appendLine: (pid, stream, line) => {
    const session = get().sessions[pid];
    if (!session) {
      // A log arrived for a session we didn't register locally (e.g. after a reload) — create a placeholder.
      set((state) => ({
        sessions: { ...state.sessions, [pid]: { pid, instanceName: `Process ${pid}`, version: "", status: "running", lines: [{ stream, line, ts: Date.now() }], startedAt: Date.now() } },
        activePid: state.activePid ?? pid,
      }));
      return;
    }
    const lines = [...session.lines, { stream, line, ts: Date.now() }];
    if (lines.length > MAX_LINES_PER_SESSION) lines.splice(0, lines.length - MAX_LINES_PER_SESSION);
    set((state) => ({ sessions: { ...state.sessions, [pid]: { ...session, lines } } }));
  },

  endSession: (pid, code) => {
    const session = get().sessions[pid];
    if (!session) return;
    set((state) => ({ sessions: { ...state.sessions, [pid]: { ...session, status: "exited", exitCode: code ?? undefined } } }));
  },

  clearSession: (pid) => {
    set((state) => {
      const next = { ...state.sessions };
      delete next[pid];
      const activePid = state.activePid === pid ? Object.keys(next).map(Number).pop() ?? null : state.activePid;
      return { sessions: next, activePid };
    });
  },
}));
