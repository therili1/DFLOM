import { useEffect, useMemo, useRef, useState } from "react";
import { Copy, Terminal, Trash2 } from "lucide-react";
import { useLogsStore } from "../stores/logsStore";

export default function Logs() {
  const sessions = useLogsStore((state) => state.sessions);
  const activePid = useLogsStore((state) => state.activePid);
  const setActivePid = useLogsStore((state) => state.setActivePid);
  const clearSession = useLogsStore((state) => state.clearSession);
  const [autoScroll, setAutoScroll] = useState(true);
  const [showLauncherLogs, setShowLauncherLogs] = useState(true);
  const scrollRef = useRef<HTMLDivElement | null>(null);

  const sessionList = useMemo(() => Object.values(sessions).sort((a, b) => b.startedAt - a.startedAt), [sessions]);
  const active = activePid != null ? sessions[activePid] : undefined;
  const shown = active ?? sessionList[0];

  // Filtered lines: hide launcher-stream lines when the toggle is off.
  const displayLines = useMemo(
    () => (shown ? (showLauncherLogs ? shown.lines : shown.lines.filter((l) => l.stream !== "launcher")) : []),
    [shown, showLauncherLogs],
  );

  useEffect(() => { if (!activePid && sessionList[0]) setActivePid(sessionList[0].pid); }, [activePid, sessionList, setActivePid]);
  useEffect(() => {
    if (autoScroll && scrollRef.current) scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
  }, [displayLines.length, autoScroll]);

  const copyAll = () => {
    if (!shown) return;
    void navigator.clipboard.writeText(displayLines.map((entry) => entry.line).join("\n"));
  };

  return (
    <div className="logs-page">
      <div className="page-title"><div><span className="eyebrow"><Terminal size={13} /> DIAGNOSTICS</span><h1>Logs</h1><p>Live console output from launched Minecraft instances.</p></div></div>

      {!sessionList.length ? (
        <div className="java-empty"><Terminal size={23} /><h3>No log sessions yet</h3><p>Launch an instance to see live game output here.</p></div>
      ) : (
        <>
          <div className="log-session-tabs">
            {sessionList.map((session) => (
              <button
                key={session.pid}
                className={`log-session-tab ${shown?.pid === session.pid ? "active" : ""}`}
                onClick={() => setActivePid(session.pid)}
              >
                <span className={`log-status-dot ${session.status}`} />
                {session.instanceName} <small>#{session.pid}</small>
              </button>
            ))}
          </div>

          {shown && (
            <section className="log-panel">
              <div className="log-panel-header">
                <div>
                  <strong>{shown.instanceName}</strong>
                  <span className="log-panel-meta">
                    {shown.version} · PID {shown.pid} ·{" "}
                    {shown.status === "running" ? "Running" : `Exited${shown.exitCode != null ? ` (code ${shown.exitCode})` : ""}`}
                  </span>
                </div>
                <div className="log-panel-actions">
                  <label className="log-autoscroll">
                    <input type="checkbox" checked={showLauncherLogs} onChange={(e) => setShowLauncherLogs(e.target.checked)} />
                    Launcher logs
                  </label>
                  <label className="log-autoscroll">
                    <input type="checkbox" checked={autoScroll} onChange={(event) => setAutoScroll(event.target.checked)} />
                    Autoscroll
                  </label>
                  <button className="icon-action" title="Copy log" onClick={copyAll}><Copy size={15} /></button>
                  <button className="icon-action" title="Clear session" onClick={() => clearSession(shown.pid)}><Trash2 size={15} /></button>
                </div>
              </div>
              <div className="log-console" ref={scrollRef}>
                {displayLines.length === 0 ? (
                  <div className="log-console-empty">
                    {shown.lines.length > 0 ? "Launcher logs are hidden — enable the toggle above." : "Waiting for output…"}
                  </div>
                ) : (
                  displayLines.map((entry, index) => (
                    <div
                      key={index}
                      className={`log-line ${entry.stream === "stderr" ? "stderr" : entry.stream === "launcher" ? "launcher" : ""}`}
                    >
                      {entry.line}
                    </div>
                  ))
                )}
              </div>
            </section>
          )}
        </>
      )}
    </div>
  );
}
