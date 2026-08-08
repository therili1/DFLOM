import { useEffect, useRef, useState } from "react";
import { listen } from "@tauri-apps/api/event";
import { CheckSquare, ChevronDown, Download, ExternalLink, Filter, LoaderCircle, Search, Square, Store, X } from "lucide-react";
import MinecraftVersionPicker from "../components/MinecraftVersionPicker";
import ThemeMarketplace from "./ThemeMarketplace";
import { CacheService } from "../services/modrinth/CacheService";
import { CategoryService, CURSEFORGE_CLASS_ID, type MarketSource } from "../services/modrinth/CategoryService";
import { DependencyResolver, type ResolvedDependency, type UnresolvedDependency } from "../services/modrinth/DependencyResolver";
import { DownloadService } from "../services/modrinth/DownloadService";
import { searchProjects, type SearchResult } from "../services/modrinth/SearchService";
import { VersionService, type ModrinthVersion } from "../services/modrinth/VersionService";
import { type Project } from "../services/modrinth/ProjectService";
import { CurseForgeService, type CurseForgeFile, type CurseForgeMod } from "../services/curseforge/CurseForgeService";
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

// CurseForge search results don't carry structured category ids through
// the thin Edge-Function-backed type we get back (see CurseForgeService),
// so category filtering happens here by loosely matching the checked
// label against each mod's own `categories[].name`. Punctuation/case
// differences are normalized away so e.g. "Combat / PvP" still matches a
// mod whose category is literally named "Combat/PvP".
function normalizeLabel(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, "");
}
function matchesCurseForgeCategories(item: CurseForgeMod, selected: Set<string>): boolean {
  if (selected.size === 0) return true;
  const modLabels = (item.categories ?? []).map((category) => normalizeLabel(category.name));
  return Array.from(selected).some((label) => modLabels.includes(normalizeLabel(label)));
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

  // Where to search: Modrinth (default, full install pipeline) or
  // CurseForge (browsing + single-file installs for mods; modpacks link
  // out since there's no manifest-import pipeline for CurseForge's zip
  // format yet).
  const [source, setSource] = useState<MarketSource>("modrinth");
  const [cfResult, setCfResult] = useState<CurseForgeMod[] | null>(null);
  const [cfLoading, setCfLoading] = useState(false);
  const [cfError, setCfError] = useState<string | null>(null);
  const [cfOpenId, setCfOpenId] = useState<number | null>(null);
  const [cfFiles, setCfFiles] = useState<CurseForgeFile[]>([]);
  const [cfFilesLoading, setCfFilesLoading] = useState(false);
  const [cfFilesError, setCfFilesError] = useState<string | null>(null);
  const [cfInstallingId, setCfInstallingId] = useState<number | null>(null);

  // Category/loader filter sidebar (checkboxes), same shape on both tabs
  // that support it (Mods / Modpacks) but sourced from a different list
  // depending on `source`.
  const [showFilters, setShowFilters] = useState(false);
  const [selectedCategories, setSelectedCategories] = useState<Set<string>>(new Set());

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

  const categoryOptions = CategoryService.categoriesFor(type, source);
  const loadersAvailable = CategoryService.loadersAvailable(source);
  const isCurseforge = source === "curseforge";

  const load = async () => {
    setLoading(true); setError(null);
    const categories = Array.from(selectedCategories);
    const key = JSON.stringify({ type, query, version, loader, sort, page, categories });
    try {
      const cached = CacheService.get<SearchResult>(key);
      const data = cached ?? await searchProjects({ query, projectType: type, version, loader, sort, offset: page * 20, categories });
      if (!cached) CacheService.set(key, data);
      setResult(data);
    } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); }
    finally { setLoading(false); }
  };

  const loadCurseforge = async () => {
    setCfLoading(true); setCfError(null);
    try {
      const classId = CURSEFORGE_CLASS_ID[type];
      const data = await CurseForgeService.search(query, classId);
      setCfResult(data.data ?? []);
    } catch (reason) { setCfError(reason instanceof Error ? reason.message : String(reason)); }
    finally { setCfLoading(false); }
  };

  useEffect(() => {
    // Auto-fill the search filters from whatever instance the user picks as
    // the install target, so browsing already matches that instance's game
    // version/loader instead of requiring the user to set them by hand
    // (and, per the version-source bug above, without this the version
    // filter and the instance's actual version could silently disagree).
    if (!selectedInstance) return;
    if (selectedInstance.minecraftVersion) setVersion(selectedInstance.minecraftVersion);
    if (selectedInstance.loader) setLoader(selectedInstance.loader);
    setPage(0);
  }, [selectedInstance?.name]);

  useEffect(() => {
    if (type === "theme") return;
    if (isCurseforge) void loadCurseforge(); else void load();
  }, [type, version, loader, sort, page, source, selectedCategories]);
  useEffect(() => { void loadInstances(); }, [loadInstances]);
  useEffect(() => { setOpenProjectId(null); setCfOpenId(null); setSelectedCategories(new Set()); }, [type]);
  useEffect(() => {
    // CurseForge browsing isn't available for every tab (only Mods /
    // Modpacks are wired up) -- fall back to Modrinth automatically so the
    // page never gets stuck showing an empty CurseForge grid for a tab it
    // can't search.
    if (isCurseforge && !CategoryService.curseforgeCapableTypes.has(type)) setSource("modrinth");
  }, [type, isCurseforge]);
  useEffect(() => { setSelectedCategories(new Set()); }, [source]);

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

  const toggleCategory = (category: string) => {
    setSelectedCategories((state) => {
      const next = new Set(state);
      if (next.has(category)) next.delete(category); else next.add(category);
      return next;
    });
    setPage(0);
  };

  const resolveDependencies = async (project: Project, chosen: ModrinthVersion) => {
    setDependencies([]); setSelectedDeps(new Set()); setUnresolvedDeps([]);
    if (type === "modpack") return; // .mrpack archives already bundle their dependencies
    setDependenciesLoading(true);
    const targetVersion = selectedInstance?.minecraftVersion || version || undefined;
    const targetLoader = selectedInstance?.loader || loader || undefined;
    try {
      const { resolved, unresolved } = await DependencyResolver.resolveRequired(chosen, project.project_id, targetVersion, targetLoader);
      setDependencies(resolved);
      setSelectedDeps(new Set(resolved.map((dep) => dep.project.project_id)));
      setUnresolvedDeps(unresolved);
    } catch {
      // Best-effort: if resolution fails, just don't show suggested dependencies.
    } finally {
      setDependenciesLoading(false);
    }
  };

  // Must match the key building logic in toggleVersions exactly, so reads
  // (render, confirmInstall) hit the same cache entry the fetch wrote to.
  const versionsCacheKey = (project: Project) => {
    const targetVersion = type === "modpack" ? version : (selectedInstance?.minecraftVersion || version);
    const targetLoader = type === "modpack" ? loader : (selectedInstance?.loader || loader);
    return `${project.project_id}::${targetVersion || ""}::${targetLoader || ""}`;
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
    // Prefer the *target instance's* actual Minecraft version / loader over
    // the search bar's filters -- those are for browsing, and may be empty
    // or set to something else entirely, which was picking the wrong
    // version for the instance the mod is actually going into.
    const targetVersion = type === "modpack" ? version : (selectedInstance?.minecraftVersion || version);
    const targetLoader = type === "modpack" ? loader : (selectedInstance?.loader || loader);
    // Cache is keyed by project + the filters actually sent to Modrinth, not
    // just the project id -- otherwise switching the game version/loader
    // after a project was already opened once would keep showing the stale
    // list fetched under the previous filters.
    const cacheKey = versionsCacheKey(project);
    if (!versionsByProject[cacheKey]) {
      setVersionsLoading(true); setVersionsError(null);
      try {
        // Ask Modrinth to filter server-side first. Only fall back to a
        // broader query (dropping the loader, then dropping all filters) if
        // the strict query comes back empty -- each tier is still a real,
        // filtered API call, never a client-side reuse of an unfiltered list.
        let list = await VersionService.list(project.project_id, targetVersion ? [targetVersion] : undefined, targetLoader ? [targetLoader] : undefined);
        if (list.length === 0 && targetLoader) {
          list = await VersionService.list(project.project_id, targetVersion ? [targetVersion] : undefined, undefined);
        }
        if (list.length === 0) {
          list = await VersionService.list(project.project_id);
        }
        setVersionsByProject((state) => ({ ...state, [cacheKey]: list }));
        const preferred = list.find((item) => (!targetVersion || item.game_versions.includes(targetVersion)) && (!targetLoader || item.loaders.includes(targetLoader)))
          ?? list.find((item) => !targetVersion || item.game_versions.includes(targetVersion))
          ?? list[0];
        if (preferred) chooseVersion(project, list, preferred.id);
      } catch (reason) { setVersionsError(reason instanceof Error ? reason.message : String(reason)); }
      finally { setVersionsLoading(false); }
    } else {
      const list = versionsByProject[cacheKey];
      const preferred = list.find((item) => (!targetVersion || item.game_versions.includes(targetVersion)) && (!targetLoader || item.loaders.includes(targetLoader)))
        ?? list.find((item) => !targetVersion || item.game_versions.includes(targetVersion))
        ?? list[0];
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
    const versions = versionsByProject[versionsCacheKey(project)] ?? [];
    const chosen = versions.find((item) => item.id === selectedVersionId);
    if (!chosen) { setError("Select a version first."); return; }
    setInstallingId(project.project_id); setError(null); setStatus(null);
    setProgress(null); setSpeedBps(null); setEtaSeconds(null); lastSampleRef.current = null;
    try {
      if (type === "modpack") {
        const name = newInstanceName.trim();
        if (!name) throw new Error("Enter a name for the new instance.");
        await DownloadService.installModpackVersion(chosen, name, project.icon_url || undefined);
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

  // --- CurseForge card actions -------------------------------------------------

  const toggleCfFiles = async (mod: CurseForgeMod) => {
    setStatus(null); setCfError(null);
    if (cfOpenId === mod.id) { setCfOpenId(null); return; }
    setCfOpenId(mod.id);
    setNewInstanceName(mod.name);
    setCfFiles([]); setCfFilesError(null); setCfFilesLoading(true);
    try {
      const data = await CurseForgeService.getFiles(mod.id);
      setCfFiles(data.data ?? []);
    } catch (reason) { setCfFilesError(reason instanceof Error ? reason.message : String(reason)); }
    finally { setCfFilesLoading(false); }
  };

  const confirmCfInstall = async (mod: CurseForgeMod, file: CurseForgeFile) => {
    if (type === "modpack") {
      // Full pipeline: download zip -> manifest.json -> mods -> overrides
      // -> ready instance (see install_curseforge_modpack in curseforge.rs).
      const name = newInstanceName.trim();
      if (!name) { setError("Enter a name for the new instance."); return; }
      setCfInstallingId(mod.id); setError(null); setStatus(null);
      setProgress(null); setSpeedBps(null); setEtaSeconds(null); lastSampleRef.current = null;
      try {
        const { instance, warnings } = await CurseForgeService.installModpack(file.downloadUrl, file.fileName, name, mod.logo?.url || mod.logo?.thumbnailUrl || undefined);
        const warningNote = warnings.length ? ` Увага: ${warnings.length} файл(ів) не вдалось завантажити (можливо, автор заборонив сторонні завантаження на CurseForge) — довстановіть їх вручну.` : "";
        setStatus(`"${mod.name}" (${file.fileName}) was installed as a new instance "${instance.name}".${warningNote}`);
        await loadInstances();
        setCfOpenId(null);
      } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); }
      finally { setCfInstallingId(null); }
      return;
    }
    if (!selectedInstance) { setError("Select a target instance first."); return; }
    setCfInstallingId(mod.id); setError(null); setStatus(null);
    setProgress(null); setSpeedBps(null); setEtaSeconds(null); lastSampleRef.current = null;
    try {
      await DownloadService.install(file.downloadUrl, selectedInstance.gameDirectory || "", type, file.fileName, file.fileLength ?? 0);
      setStatus(`"${mod.name}" (${file.fileName}) was installed into "${selectedInstance.name}".`);
      setCfOpenId(null);
    } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); }
    finally { setCfInstallingId(null); }
  };

  const cfFilteredResult = cfResult ? cfResult.filter((item) => matchesCurseForgeCategories(item, selectedCategories)) : null;

  return <div className="marketplace-page">
    <div className="page-title marketplace-header"><div><span className="eyebrow"><Store size={13} /> {isCurseforge ? "CURSEFORGE CENTER" : "MODRINTH CENTER"}</span><h1>Marketplace</h1><p>Discover community projects from the official {isCurseforge ? "CurseForge" : "Modrinth"} API.</p></div></div>

    {type !== "theme" && CategoryService.curseforgeCapableTypes.has(type) && <div className="market-source-toggle">
      <span>Джерело:</span>
      <button type="button" className={source === "modrinth" ? "active" : ""} onClick={() => setSource("modrinth")}>
        {source === "modrinth" ? <CheckSquare size={15} /> : <Square size={15} />} Modrinth
      </button>
      <button type="button" className={source === "curseforge" ? "active" : ""} onClick={() => setSource("curseforge")}>
        {source === "curseforge" ? <CheckSquare size={15} /> : <Square size={15} />} CurseForge
      </button>
    </div>}

    {type !== "modpack" && type !== "theme" && <div className="market-instance-bar">
      <span>Install target:</span>
      {instances.length ? <select value={selectedInstance?.name ?? ""} onChange={(event) => selectInstance(instances.find((item) => item.name === event.target.value) ?? null)}>
        {instances.map((item) => <option key={item.name} value={item.name}>{item.name}</option>)}
      </select> : <span className="market-instance-warning">No instances yet — create one on the Instances page first.</span>}
    </div>}

    <div className="market-tabs">{tabs.map((tab) => <button className={type === tab.value ? "active" : ""} key={tab.value} onClick={() => { setType(tab.value); setPage(0); }}>{tab.label}</button>)}</div>

    {type === "theme" ? <ThemeMarketplace /> : <>
    <section className="market-controls">
      <div className="market-search"><Search size={16} /><input value={query} onChange={(event) => setQuery(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter") { setPage(0); isCurseforge ? void loadCurseforge() : void load(); } }} placeholder={isCurseforge ? "Search CurseForge projects" : "Search Modrinth projects"} /></div>
      {!isCurseforge && <MinecraftVersionPicker value={version} onChange={(value) => { setVersion(value); setPage(0); }} placeholder="Minecraft version" />}
      {!isCurseforge && <select value={loader} onChange={(event) => { setLoader(event.target.value); setPage(0); }}><option value="">All loaders</option>{CategoryService.loaders.map((item) => <option key={item} value={item}>{item}</option>)}</select>}
      {!isCurseforge && <select value={sort} onChange={(event) => { setSort(event.target.value); setPage(0); }}><option value="relevance">Relevance</option><option value="downloads">Downloads</option><option value="newest">Newest</option><option value="updated">Updated</option><option value="name">Alphabetical</option></select>}
      <button type="button" className={`filter-toggle-button${showFilters ? " active" : ""}`} onClick={() => setShowFilters((v) => !v)}>
        <Filter size={15} /> Налаштування фільтрів{selectedCategories.size > 0 ? ` (${selectedCategories.size})` : ""}
      </button>
      <button className="primary-button" onClick={() => { setPage(0); isCurseforge ? void loadCurseforge() : void load(); }} disabled={isCurseforge ? cfLoading : loading}>{(isCurseforge ? cfLoading : loading) ? <LoaderCircle className="spin" size={15} /> : <Search size={15} />} Search</button>
    </section>

    {error && <div className="java-error">{error}</div>}
    {status && <div className="market-status">{status}</div>}

    <div className={`market-body${showFilters ? " with-filters" : ""}`}>
      {showFilters && <aside className="market-filters-panel">
        <div className="market-filters-header">
          <h3>Налаштування фільтрів</h3>
          <button type="button" onClick={() => setShowFilters(false)}><X size={15} /></button>
        </div>
        {selectedCategories.size > 0 && <button type="button" className="market-filters-clear" onClick={() => setSelectedCategories(new Set())}>Скинути фільтри</button>}
        <div className="market-filters-section">
          <h4>Категорії</h4>
          <div className="market-filters-list">
            {categoryOptions.map((category) => <label key={category} className="market-filter-checkbox">
              <input type="checkbox" checked={selectedCategories.has(category)} onChange={() => toggleCategory(category)} />
              <span>{category}</span>
            </label>)}
          </div>
        </div>
        {loadersAvailable && <div className="market-filters-section">
          <h4>Лідери</h4>
          <div className="market-filters-list">
            {CategoryService.loaders.map((item) => <label key={item} className="market-filter-checkbox">
              <input type="checkbox" checked={loader === item} onChange={() => { setLoader(loader === item ? "" : item); setPage(0); }} />
              <span>{item}</span>
            </label>)}
          </div>
        </div>}
        {isCurseforge && <p className="market-filters-note">Категорії CurseForge фільтруються на основі даних, отриманих з пошуку.</p>}
      </aside>}

      <div className="market-results">
        {isCurseforge ? <>
          {cfError && <div className="java-error">{cfError}</div>}
          {cfLoading && !cfResult ? <div className="market-grid">{Array.from({ length: 8 }, (_, i) => <div className="market-skeleton" key={i} />)}</div> : cfFilteredResult && cfFilteredResult.length ? <div className="market-grid">{cfFilteredResult.map((mod) => {
            const isOpen = cfOpenId === mod.id;
            const disabled = type !== "modpack" && !selectedInstance;
            return <article className="project-card" key={mod.id}>
              <img loading="lazy" src={mod.logo?.thumbnailUrl || mod.logo?.url || ""} alt="" />
              <div className="project-content">
                <div className="project-type">curseforge · {type}</div>
                <h2>{mod.name}</h2>
                <p>{mod.summary}</p>
                <span className="project-author">by {mod.authors?.map((author) => author.name).join(", ")}</span>
                <div className="project-meta"><span><Download size={13} /> {formatDownloads(mod.downloadCount)}</span><span>{mod.categories?.[0]?.name ?? ""}</span></div>
                <div className="project-actions">
                  <button className="primary-button" disabled={disabled} onClick={() => void toggleCfFiles(mod)}>
                    <ChevronDown size={14} style={{ transform: isOpen ? "rotate(180deg)" : undefined }} /> {isOpen ? "Close" : "Install"}
                  </button>
                  <a href={`https://www.curseforge.com/minecraft/search?search=${encodeURIComponent(mod.name)}`} target="_blank" rel="noreferrer"><ExternalLink size={14} /></a>
                </div>
                {isOpen && <div className="version-picker">
                  {cfFilesLoading ? <span className="version-picker-loading"><LoaderCircle className="spin" size={13} /> Завантаження файлів…</span> : cfFilesError ? <span className="java-error">{cfFilesError}</span> : cfFiles.length ? <>
                    <div className="version-list">
                      {cfFiles.map((file) => <button
                        key={file.id}
                        type="button"
                        className="version-list-item"
                        disabled={cfInstallingId === mod.id}
                        onClick={() => void confirmCfInstall(mod, file)}
                      >
                        <span>{file.fileName}</span>
                        <span className="version-tag">{file.gameVersions?.[0] ?? "?"}</span>
                      </button>)}
                    </div>
                    {type === "modpack" && <>
                      <label>Назва нового інстансу</label>
                      <input value={newInstanceName} onChange={(event) => setNewInstanceName(event.target.value)} placeholder="Instance name" />
                      <p className="market-filters-note" style={{ margin: "6px 0 0" }}>Натисніть на файл вище, щоб запустити встановлення: завантаження → manifest.json → моди → ресурс-паки/конфіги → готова інстанція.</p>
                    </>}
                  </> : <span>No files available for this project.</span>}
                  {cfInstallingId === mod.id && (progress ? (
                    <div className="download-progress" style={{ marginTop: 10 }}>
                      <div className="download-progress-bar" style={{ background: "#1b2431", borderRadius: 6, height: 6, overflow: "hidden" }}>
                        <div style={{
                          width: `${Math.min(100, progress.bytesTotal > 0 ? (progress.bytesDone / progress.bytesTotal) * 100 : 0)}%`,
                          background: "var(--accent)", height: "100%", transition: "width 150ms linear",
                        }} />
                      </div>
                      <div className="download-progress-meta" style={{ display: "flex", justifyContent: "space-between", fontSize: 12, opacity: 0.75, marginTop: 4, gap: 8 }}>
                        <span>{progress.fileName}{" • "}{formatBytes(progress.bytesDone)}{progress.bytesTotal > 0 ? ` / ${formatBytes(progress.bytesTotal)}` : ""}</span>
                        <span>{speedBps ? `${formatBytes(speedBps)}/s` : ""}{etaSeconds !== null ? ` • ~${formatEta(etaSeconds)}` : ""}</span>
                      </div>
                    </div>
                  ) : <span className="version-picker-loading"><LoaderCircle className="spin" size={13} /> {type === "modpack" ? "Встановлення збірки (моди, оверрайди)…" : "Встановлення…"}</span>)}
                </div>}
              </div>
            </article>;
          })}</div> : <div className="market-empty"><Store size={24} /><h2>No projects found</h2><p>Try a different search or filter.</p></div>}
        </> : <>
          {loading && !result ? <div className="market-grid">{Array.from({ length: 8 }, (_, i) => <div className="market-skeleton" key={i} />)}</div> : result?.hits.length ? <div className="market-grid">{result.hits.map((project) => {
            const isOpen = openProjectId === project.project_id;
            const versions = versionsByProject[versionsCacheKey(project)] ?? [];
            const selectedVersion = versions.find((item) => item.id === selectedVersionId);
            const disabled = type !== "modpack" && !selectedInstance;
            return <article className="project-card" key={project.project_id}>
              <img loading="lazy" src={project.icon_url || ""} alt="" />
              <div className="project-content">
                <div className="project-type">{project.project_type}</div>
                <h2>{project.title}</h2>
                <p>{project.description}</p>
                <span className="project-author">by {project.author}</span>
                <div className="project-meta"><span><Download size={13} /> {formatDownloads(project.downloads)}</span><span>{
                  // `project.versions` is the raw list of every game version the
                  // project has EVER shipped for, in Modrinth's own (not
                  // filter-aware) order -- it has nothing to do with the
                  // version/loader filters currently applied, which is why this
                  // used to show e.g. "1.16.5" for Iris even while filtering for
                  // 26.2. Prefer the version the user actually selected in the
                  // (now server-side filtered) version list; while that hasn't
                  // loaded yet, fall back to the search filter itself rather than
                  // an arbitrary unrelated entry from the project's full history.
                  selectedVersion?.game_versions[0] ?? (openProjectId === project.project_id && versionsLoading ? "…" : (version || "All versions"))
                }</span></div>
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
      </div>
    </div>
    </>}
  </div>;
}
