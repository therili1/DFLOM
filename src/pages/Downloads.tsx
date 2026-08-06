import { Download, LoaderCircle, Pause, Play, RefreshCw, X } from "lucide-react";
import { useState } from "react";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { isTauri } from "@tauri-apps/api/core";
import { useEffect } from "react";
import { InstanceManager } from "../services/InstanceManager";
import { useInstanceStore } from "../stores/InstanceStore";
import { useVersionStore } from "../stores/versionStore";
import { MinecraftDownloader } from "../services/downloader/MinecraftDownloader";
import type { DownloadTask } from "../services/downloader/DownloadTask";

export default function Downloads() {
  const { refreshInstances } = useInstanceStore();
  const { versions, refreshVersions } = useVersionStore();
  const [tasks, setTasks] = useState<DownloadTask[]>([]);
  const [version, setVersion] = useState("");
  const [directory, setDirectory] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [dropActive, setDropActive] = useState(false);
  const importMrpack = async (archivePath: string) => {
    const filename = archivePath.split(/[\\/]/).pop() ?? "Imported Modpack.mrpack";
    const instanceName = filename.replace(/\.mrpack$/i, "").trim() || "Imported Modpack";
    try {
      await InstanceManager.importMrpack(archivePath, instanceName);
      await refreshInstances();
      setError(null);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : String(reason));
    }
  };
  useEffect(() => {
    if (!isTauri()) return;
    let unlisten: (() => void) | undefined;
    void getCurrentWindow().onDragDropEvent((event) => {
      if (event.payload.type === "enter" || event.payload.type === "over") {
        setDropActive(true);
      } else if (event.payload.type === "leave") {
        setDropActive(false);
      } else if (event.payload.type === "drop") {
        setDropActive(false);
        for (const path of event.payload.paths) {
          if (/\.mrpack$/i.test(path)) void importMrpack(path);
        }
      }
    }).then((dispose) => { unlisten = dispose; });
    return () => { unlisten?.(); };
  }, []);
  const start = async () => {
    const item = versions.find((entry) => entry.id === version);
    if (!item || !directory.trim()) { setError("Select a Mojang version and enter an instance directory."); return; }
    const task: DownloadTask = { id: `${item.id}-${Date.now()}`, version: item.id, status: "downloading", progress: 0, downloadedBytes: 0, totalBytes: 0 };
    setTasks((current) => [task, ...current]); setError(null);
    try { const completed = await MinecraftDownloader.download(task, item.url, directory.trim()); setTasks((current) => current.map((entry) => entry.id === task.id ? completed : entry)); }
    catch (reason) { setTasks((current) => current.map((entry) => entry.id === task.id ? { ...entry, status: "failed", error: String(reason) } : entry)); }
  };
  const formatBytes = (bytes: number) => bytes < 1024 * 1024 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  return <div className="downloads-page"><div className="page-title"><span className="eyebrow"><Download size={13} /> CONTENT DELIVERY</span><h1>Downloads</h1><p>Download official Minecraft files into an instance directory.</p></div>
    <section className={`download-dropzone ${dropActive ? "active" : ""}`}><Download size={22} /><strong>Drop a .mrpack file here</strong><span>Release a Modrinth modpack anywhere over this window to import it.</span></section>
    <section className="download-builder"><div className="section-heading"><div><span className="eyebrow">OFFICIAL MOJANG FILES</span><h2>Download Version</h2></div><button className="secondary-button" onClick={() => void refreshVersions()}><RefreshCw size={14} /> Refresh versions</button></div><div className="download-form"><label>Version<select value={version} onChange={(event) => setVersion(event.target.value)}><option value="">Select version</option>{versions.map((item) => <option key={item.id} value={item.id}>{item.id} · {item.type}</option>)}</select></label><label>Instance directory<input value={directory} onChange={(event) => setDirectory(event.target.value)} placeholder="Path to instance directory" /></label><button className="primary-button" onClick={() => void start()}><Download size={15} /> Download Version</button></div>{!versions.length && <p className="download-hint">Open Create Instance once or refresh versions to load the official Mojang manifest.</p>}{error && <div className="java-error">{error}</div>}</section>
    <section className="download-queue"><div className="section-heading"><div><span className="eyebrow">TRANSFER QUEUE</span><h2>Queue</h2></div></div>{!tasks.length ? <div className="java-empty"><Download size={23} /><h3>No downloads yet</h3><p>Completed and active version downloads will appear here.</p></div> : tasks.map((task) => <article className="download-task" key={task.id}><div className="download-task-icon">{task.status === "downloading" ? <LoaderCircle className="spin" size={18} /> : <Download size={18} />}</div><div className="download-task-main"><div className="download-task-title"><strong>Minecraft {task.version}</strong><span className={`download-status ${task.status}`}>{task.status}</span></div><div className="download-progress"><i style={{ width: `${task.progress}%` }} /></div><small>{task.error || (task.status === "completed" ? `${formatBytes(task.downloadedBytes)} downloaded` : "Downloading client, libraries, assets and logging configuration")}</small></div><button className="icon-action" title="Cancel" onClick={() => setTasks((current) => current.map((entry) => entry.id === task.id ? { ...entry, status: "cancelled" } : entry))}><X size={15} /></button></article>)}</section>
  </div>;
}