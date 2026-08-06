import { useEffect, useRef, useState } from "react";
import { listen } from "@tauri-apps/api/event";
import { ChevronDown, Download, ExternalLink, LoaderCircle, Search, Store } from "lucide-react";
import MinecraftVersionPicker from "../components/MinecraftVersionPicker";
import ThemeMarketplace from "./ThemeMarketplace";
import { CacheService } from "../services/modrinth/CacheService";
import { CategoryService } from "../services/modrinth/CategoryService";
import { DependencyResolver, type ResolvedDependency, type UnresolvedDependency } from "../services/modrinth/DependencyResolver";
import { DownloadService } from "../services/modrinth/DownloadService";
import { searchProjects, type SearchResult } from "../services/modrinth/SearchService";
import { VersionService, type ModrinthVersion } from "../services/modrinth/VersionService";
import { type Project } from "../services/modrinth/ProjectService";
import { useInstanceStore } from "../stores/InstanceStore";

const tabs = [{ value: "mod", label: "Mods" }, { value: "modpack", label: "Modpacks" }, { value: "shader", label: "Shaders" }, { value: "resourcepack", label: "Resource Packs" }, { value: "datapack", label: "Data Packs" }, { value: "theme", label: "Themes" }];

type DownloadProgressEvent = {
  taskId: string;
  fileName: string;
  fileIndex: number;
  fileTotal: number;
  bytesDone: number;
  bytesTotal: number;
  done: boolean;
};

// Rust only reports raw counters (bytes done / total, per-event) -- speed
// and ETA are derived here from consecutive events, smoothed with a light
// exponential moving average so the number on screen doesn't jump around
// on every 64KB chunk.
function formatEta(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return "...";
  if (seconds < 1) return "<1s";
  if (seconds < 60) return `${Math.ceil(seconds)}s`;
  const minutes = Math.floor(seconds / 60);
  const rest = Math.ceil(seconds % 60);
  return `${minutes}m ${rest}s`;
}
function formatBytes(bytes: number): string {
  if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  if (bytes >= 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${bytes} B`;
}

export default function Marketplace() {
  const [type, setType] = useState("mod");
  const [query, setQuery] = useState("");
  const [version, setVersion] = useState("");
  const [loader, setLoader] = useState("");
  const [sort, setSort] = useState("relevance");
  const [page, setPage] = useState(0);
  const [result, setResult] = useState<SearchResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);

  // Version-picker state: which project card is expanded, its available versions, and the user's choice.
  const [openProjectId, setOpenProjectId] = useState<string | null>(null);
  const [versionsByProject, setVersionsByProject] = useState<Record<string, ModrinthVersion[]>>({});
  const [versionsLoading, setVersionsLoading] = useState(false);
  const [versionsError, setVersionsError] = useState<string | null>(null);
  const [selectedVersionId, setSelectedVersionId] = useState<string>("");
  const [installingId, setInstallingId] = useState<string | null>(null);
  const [newInstanceName, setNewInstanceName] = useState("");
  const [progress, setProgress] = useState<DownloadProgressEvent | null>(null);
  const [etaSeconds, setEtaSeconds] = useState<number | null>(null);
  const [speedBps, setSpeedBps] = useState<number | null>(null);
  const lastSampleRef = useRef<{ time: number; bytesDone: number } | null>(null);

  // Dependency-picker state: required dependencies of the currently selected version.
  const [dependencies, setDependencies] = useState<ResolvedDependency[]>([]);
  const [unresolvedDeps, setUnresolvedDeps] = useState<UnresolvedDependency[]>([]);
  const [dependenciesLoading, setDependenciesLoading] = useState(false);
  const [selectedDeps, setSelectedDeps] = useState<Set<string>>(new Set());

  const { instances, selectedInstance, selectInstance, loadInstances } = useInstanceStore();

  const load = async () => {
    setLoading(true); setError(null);
    const key = JSON.stringify({ type, query, version, loader, sort, page });
    try {
      const cached = CacheService.get<SearchResult>(key);
      const data = cached ?? await searchProjects({ query, projectType: type, version, loader, sort, offset: page * 20 });
      if (!cached) CacheService.set(key, data);
      setResult(data);
    } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); }
    finally { setLoading(false); }
  };
  useEffect(() => { void load(); }, [type, version, loader, sort, page]);
  useEffect(() => { void loadInstances(); }, [loadInstances]);
  useEffect(() => { setOpenProjectId(null); }, [type]);

  // Real progress from the backend (see download_checked_streaming /
  // emit_progress in lib.rs) -- one event per chunk on the file currently
  // downloading, plus one per completed file. Speed/ETA are computed here
  // from consecutive events with a light exponential moving average so the
  // number doesn't jump around on every ~64KB chunk.
  useEffect(() => {
    const unlisten = listen<DownloadProgressEvent>("download-progress", (event) => {
      const payload = event.payload;
      setProgress(payload);
      const now = performance.now();
      const last = lastSampleRef.current;
      if (last) {
        const deltaTime = (now - last.time) / 1000;
        const deltaBytes = payload.bytesDone - last.bytesDone;
        if (deltaTime > 0 && deltaBytes >= 0) {
          const instantSpeed = deltaBytes / deltaTime;
          setSpeedBps((previous) => (previous === null ? instantSpeed : previous * 0.7 + instantSpeed * 0.3));
        }
      }
      lastSampleRef.current = { time: now, bytesDone: payload.bytesDone };
      if (payload.done && payload.fileIndex >= payload.fileTotal) {
        setTimeout(() => { setProgress(null); setSpeedBps(null); lastSampleRef.current = null; }, 600);
      }
    });
    return () => { void unlisten.then((fn) => fn()); };
  }, []);

  useEffect(() => {
    if (!progress || !speedBps || speedBps <= 0) { setEtaSeconds(null); return; }
    const remaining = Math.max(0, progress.bytesTotal - progress.bytesDone);
    setEtaSeconds(remaining / speedBps);
  }, [progress, speedBps]);

  const formatDownloads = (n: number) => n > 1000000 ? `${(n / 1000000).toFixed(1)}M` : n > 1000 ? `${(n / 1000).toFixed(1)}K` : String(n);

  const resolveDependencies = async (project: Project, chosen: ModrinthVersion) => {
    setDependencies([]); setSelectedDeps(new Set()); setUnresolvedDeps([]);
    if (type === "modpack") return; // .mrpack archives already bundle their dependencies
    setDependenciesLoading(true);
    try {
      const { resolved, unresolved } = await DependencyResolver.resolveRequired(chosen, project.project_id, version || undefined, loader || undefined);
      setDependencies(resolved);
      setSelectedDeps(new Set(resolved.map((dep) => dep.project.project_id)));
      setUnresolvedDeps(unresolved);
    } catch {
      // Best-effort: if resolution fails, just don't show suggested dependencies.
    } finally {
      setDependenciesLoading(false);
    }
  };

  const chooseVersion = (project: Project, list: ModrinthVersion[], versionId: string) => {
    setSelectedVersionId(versionId);
    const chosen = list.find((item) => item.id === versionId);
    if (chosen) void resolveDependencies(project, chosen);
  };

  const toggleVersions = async (project: Project) => {
    setStatus(null); setError(null);
    if (openProjectId === project.project_id) { setOpenProjectId(null); return; }
    setOpenProjectId(project.project_id);
    setSelectedVersionId("");
    setDependencies([]); setSelectedDeps(new Set()); setUnresolvedDeps([]);
    setNewInstanceName(project.title);
    if (!versionsByProject[project.project_id]) {
      setVersionsLoading(true); setVersionsError(null);
      try {
        const list = await VersionService.list(project.project_id);
        setVersionsByProject((state) => ({ ...state, [project.project_id]: list }));
        const preferred = list.find((item) => (!version || item.game_versions.includes(version)) && (!loader || item.loaders.includes(loader))) ?? list[0];
        if (preferred) chooseVersion(project, list, preferred.id);
      } catch (reason) { setVersionsError(reason instanceof Error ? reason.message : String(reason)); }
      finally { setVersionsLoading(false); }
    } else {
      const list = versionsByProject[project.project_id];
      const preferred = list.find((item) => (!version || item.game_versions.includes(version)) && (!loader || item.loaders.includes(loader))) ?? list[0];
      if (preferred) chooseVersion(project, list, preferred.id);
    }
  };

  const toggleDep = (projectId: string) => {
    setSelectedDeps((state) => {
      const next = new Set(state);
      if (next.has(projectId)) next.delete(projectId); else next.add(projectId);
      return next;
    });
  };

  const confirmInstall = async (project: Project) => {
    const versions = versionsByProject[project.project_id] ?? [];
    const chosen = versions.find((item) => item.id === selectedVersionId);
    if (!chosen) { setError("Select a version first."); return; }
    setInstallingId(project.project_id); setError(null); setStatus(null);
    setProgress(null); setSpeedBps(null); setEtaSeconds(null); lastSampleRef.current = null;
    try {
      if (type === "modpack") {
        const name = newInstanceName.trim();
        if (!name) throw new Error("Enter a name for the new instance.");
        await DownloadService.installModpackVersion(chosen, name);
        setStatus(`"${project.title}" (${chosen.version_number}) was installed as a new instance "${name}".`);
        await loadInstances();
      } else {
        if (!selectedInstance) throw new Error("Select a target instance first.");
        await DownloadService.installVersion(chosen, selectedInstance.gameDirectory || "", type);
        const toInstall = dependencies.filter((dep) => selectedDeps.has(dep.project.project_id));
        const failed: string[] = [];
        for (const dep of toInstall) {
          try { await DownloadService.installVersion(dep.version, selectedInstance.gameDirectory || "", dep.project.project_type); }
          catch { failed.push(dep.project.title); }
        }
        const depsNote = toInstall.length ? ` + ${toInstall.length - failed.length}/${toInstall.length} dependencies` : "";
        setStatus(`"${project.title}" (${chosen.version_number})${depsNote} was installed into "${selectedInstance.name}".${failed.length ? ` Failed: ${failed.join(", ")}.` : ""}`);
      }
      setOpenProjectId(null);
    } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); }
    finally { setInstallingId(null); }
  };

  return <div className="marketplace-page">
    <div className="page-title marketplace-header"><div><span className="eyebrow"><Store size={13} /> MODRINTH CENTER</span><h1>Marketplace</h1><p>Discover community projects from the official Modrinth API.</p></div></div>

    {type !== "modpack" && type !== "theme" && <div className="market-instance-bar">
      <span>Install target:</span>
      {instances.length ? <select value={selectedInstance?.name ?? ""} onChange={(event) => selectInstance(instances.find((item) => item.name === event.target.value) ?? null)}>
        {instances.map((item) => <option key={item.name} value={item.name}>{item.name}</option>)}
      </select> : <span className="market-instance-warning">No instances yet — create one on the Instances page first.</span>}
    </div>}

    <div className="market-tabs">{tabs.map((tab) => <button className={type === tab.value ? "active" : ""} key={tab.value} onClick={() => { setType(tab.value); setPage(0); }}>{tab.label}</button>)}</div>

    {type === "theme" ? <ThemeMarketplace /> : <>
    <section className="market-controls"><div className="market-search"><Search size={16} /><input value={query} onChange={(event) => setQuery(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter") { setPage(0); void load(); } }} placeholder="Search Modrinth projects" /></div><MinecraftVersionPicker value={version} onChange={(value) => { setVersion(value); setPage(0); }} placeholder="Minecraft version" /><select value={loader} onChange={(event) => { setLoader(event.target.value); setPage(0); }}><option value="">All loaders</option>{CategoryService.loaders.map((item) => <option key={item} value={item}>{item}</option>)}</select><select value={sort} onChange={(event) => { setSort(event.target.value); setPage(0); }}><option value="relevance">Relevance</option><option value="downloads">Downloads</option><option value="newest">Newest</option><option value="updated">Updated</option><option value="name">Alphabetical</option></select><button className="primary-button" onClick={() => { setPage(0); void load(); }} disabled={loading}>{loading ? <LoaderCircle className="spin" size={15} /> : <Search size={15} />} Search</button></section>

    {error && <div className="java-error">{error}</div>}
    {status && <div className="market-status">{status}</div>}

    {loading && !result ? <div className="market-grid">{Array.from({ length: 8 }, (_, i) => <div className="market-skeleton" key={i} />)}</div> : result?.hits.length ? <div className="market-grid">{result.hits.map((project) => {
      const isOpen = openProjectId === project.project_id;
      const versions = versionsByProject[project.project_id] ?? [];
      const selectedVersion = versions.find((item) => item.id === selectedVersionId);
      const disabled = type !== "modpack" && !selectedInstance;
      return <article className="project-card" key={project.project_id}>
        <img loading="lazy" src={project.icon_url || ""} alt="" />
        <div className="project-content">
          <div className="project-type">{project.project_type}</div>
          <h2>{project.title}</h2>
          <p>{project.description}</p>
          <span className="project-author">by {project.author}</span>
          <div className="project-meta"><span><Download size={13} /> {formatDownloads(project.downloads)}</span><span>{project.versions?.[0] || "All versions"}</span></div>
          <div className="project-actions">
            <button className="primary-button" disabled={disabled} onClick={() => void toggleVersions(project)}>
              <ChevronDown size={14} style={{ transform: isOpen ? "rotate(180deg)" : undefined }} /> {isOpen ? "Close" : "Install"}
            </button>
            <a href={`https://modrinth.com/${project.project_type}/${project.slug}`} target="_blank" rel="noreferrer"><ExternalLink size={14} /></a>
          </div>
          {isOpen && <div className="version-picker">
            {versionsLoading ? <span className="version-picker-loading"><LoaderCircle className="spin" size={13} /> Завантаження версій…</span> : versionsError ? <span className="java-error">{versionsError}</span> : versions.length ? <>
              <div className="version-list">
                {versions.map((item) => <button
                  key={item.id}
                  type="button"
                  className={`version-list-item${item.id === selectedVersionId ? " selected" : ""}`}
                  onClick={() => chooseVersion(project, versions, item.id)}
                >
                  <span>{item.version_number} for {item.game_versions[0] ?? "?"}</span>
                  <span className={`version-tag version-tag-${item.version_type}`}>[{item.version_type}]</span>
                </button>)}
              </div>
              {selectedVersion && <div className="version-selected-line">Вибрана версія: <b>{selectedVersion.version_number} for {selectedVersion.game_versions[0] ?? "?"}</b></div>}
              {type !== "modpack" && (dependenciesLoading ? <span className="version-picker-loading"><LoaderCircle className="spin" size={13} /> Перевірка залежностей…</span> : (dependencies.length > 0 || unresolvedDeps.length > 0) && <div className="dependency-list">
                {dependencies.length > 0 && <label>Потрібні також (обов'язкові залежності)</label>}
                {dependencies.map((dep) => <label key={dep.project.project_id} className="dependency-item">
                  <input type="checkbox" checked={selectedDeps.has(dep.project.project_id)} onChange={() => toggleDep(dep.project.project_id)} />
                  <span>{dep.project.title}</span>
                  <span className="dependency-version">{dep.version.version_number}</span>
                </label>)}
                {unresolvedDeps.length > 0 && <div className="version-error">Не вдалось автоматично підібрати: {unresolvedDeps.map((dep) => dep.title || dep.projectId).join(", ")}. Встановіть їх вручну через пошук.</div>}
              </div>)}
              {type === "modpack" && <>
                <label>Назва нового інстансу</label>
                <input value={newInstanceName} onChange={(event) => setNewInstanceName(event.target.value)} placeholder="Instance name" />
              </>}
              <button className="primary-button" disabled={installingId === project.project_id || !selectedVersionId} onClick={() => void confirmInstall(project)}>
                {installingId === project.project_id ? <LoaderCircle className="spin" size={14} /> : <Download size={14} />} Встановити
              </button>
              {installingId === project.project_id && progress && (
                <div className="download-progress" style={{ marginTop: 10 }}>
                  <div className="download-progress-bar" style={{ background: "#1b2431", borderRadius: 6, height: 6, overflow: "hidden" }}>
                    <div style={{
                      width: `${Math.min(100, progress.bytesTotal > 0 ? (progress.bytesDone / progress.bytesTotal) * 100 : 0)}%`,
                      background: "var(--accent)", height: "100%", transition: "width 150ms linear",
                    }} />
                  </div>
                  <div className="download-progress-meta" style={{ display: "flex", justifyContent: "space-between", fontSize: 12, opacity: 0.75, marginTop: 4, gap: 8 }}>
                    <span>
                      {progress.fileTotal > 1 ? `Файл ${Math.min(progress.fileIndex + 1, progress.fileTotal)}/${progress.fileTotal}: ${progress.fileName}` : progress.fileName}
                      {" • "}{formatBytes(progress.bytesDone)}{progress.bytesTotal > 0 ? ` / ${formatBytes(progress.bytesTotal)}` : ""}
                    </span>
                    <span>{speedBps ? `${formatBytes(speedBps)}/s` : ""}{etaSeconds !== null ? ` • ~${formatEta(etaSeconds)}` : ""}</span>
                  </div>
                </div>
              )}
            </> : <span>No versions available for this project.</span>}
          </div>}
        </div>
      </article>;
    })}</div> : <div className="market-empty"><Store size={24} /><h2>No projects found</h2><p>Try a different search or filter.</p></div>}
    {result && result.total_hits > 20 && <div className="pagination"><button disabled={page === 0} onClick={() => setPage((p) => p - 1)}>Previous</button><span>Page {page + 1} of {Math.ceil(result.total_hits / 20)}</span><button disabled={(page + 1) * 20 >= result.total_hits} onClick={() => setPage((p) => p + 1)}>Next</button></div>}
    </>}
  </div>;
}
