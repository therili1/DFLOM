import { useEffect, useState } from "react";
import { Blocks, Check, Copy, Eye, FolderOpen, LoaderCircle, MoreHorizontal, Pencil, Plus, Rocket, Search, SlidersHorizontal, Terminal, Trash2, Upload, UserRound, X } from "lucide-react";
import { convertFileSrc } from "@tauri-apps/api/core";
import { InstanceManager, type Instance } from "../services/InstanceManager";
import { useInstanceStore } from "../stores/InstanceStore";
import { useVersionStore } from "../stores/versionStore";
import { useJavaStore } from "../stores/javaStore";
import { LauncherService, type LaunchCommand } from "../services/launcher/LauncherService";
import { MemoryManager } from "../services/launcher/MemoryManager";
import type { LaunchProfile } from "../services/launcher/LaunchProfile";
import { useAccountStore } from "../stores/accountStore";
import type { Account } from "../services/accounts/OfflineAccount";
import { DownloadQueue } from "../services/downloader/DownloadQueue";
import type { DownloadTask } from "../services/downloader/DownloadTask";
import { invoke } from "@tauri-apps/api/core";
import { useLogsStore } from "../stores/logsStore";
import { InstanceContentService, type InstanceContentSummary } from "../services/instance/InstanceContentService";
import { JavaRuntime } from "../services/java/JavaRuntime";
import { javaMajor } from "../services/java/JavaValidator";

const loaders = ["Vanilla", "Fabric", "Forge", "NeoForge", "Quilt"];

export default function Instances() {
  const { instances, loading, error, loadInstances, createInstance, deleteInstance, renameInstance, selectInstance } = useInstanceStore();
  const { versions, latestRelease, loading: versionsLoading, error: versionsError, refreshVersions } = useVersionStore();
  const { javaInstallations, selectedJava, scanJava } = useJavaStore();
  const selectedAccount = useAccountStore((state) => state.selectedAccount);
  const memory = MemoryManager.getInfo();
  const [launchCommand, setLaunchCommand] = useState<LaunchCommand | null>(null);
  const [ramMax, setRamMax] = useState(memory.recommendedMemoryMb);
  const [ramMin, setRamMin] = useState(512);
  const [width, setWidth] = useState(1280);
  const [height, setHeight] = useState(720);
  const [modal, setModal] = useState(false);
  const [name, setName] = useState("");
  const [version, setVersion] = useState("1.21.8");
  const [loader, setLoader] = useState("Vanilla");
  const [loaderVersion, setLoaderVersion] = useState("");
  const [loaderVersions, setLoaderVersions] = useState<string[]>([]);
  const [loaderVersionsLoading, setLoaderVersionsLoading] = useState(false);
  const [loaderVersionsError, setLoaderVersionsError] = useState<string | null>(null);
  const [gameDirectory, setGameDirectory] = useState("");
  const [javaArgs, setJavaArgs] = useState("");
  const [editing, setEditing] = useState<string | null>(null);
  const [newName, setNewName] = useState("");
  const [versionFilter, setVersionFilter] = useState<"all" | "release" | "snapshot" | "old_alpha" | "old_beta">("all");
  const [versionSearch, setVersionSearch] = useState("");
  const [launching, setLaunching] = useState<string | null>(null);
  const [launchError, setLaunchError] = useState<string | null>(null);
  const [launchStatus, setLaunchStatus] = useState<string | null>(null);
  const { accounts } = useAccountStore();
  const [playWithFor, setPlayWithFor] = useState<string | null>(null);
  const [contentModal, setContentModal] = useState<{ name: string; mode: "view" | "edit" } | null>(null);
  const [contentSummary, setContentSummary] = useState<InstanceContentSummary | null>(null);
  const [contentLoading, setContentLoading] = useState(false);
  const [contentError, setContentError] = useState<string | null>(null);
  const [addingCategory, setAddingCategory] = useState<"mod" | "resourcepack" | "shader" | null>(null);

  // Library toolbar (search / sort / loader filter) -- becomes essential
  // once someone has more than a handful of instances, otherwise there's
  // no way to find one build among many identically-shaped cards other
  // than scrolling and reading every title.
  const [librarySearch, setLibrarySearch] = useState("");
  const [librarySort, setLibrarySort] = useState<"recent" | "name" | "size">("recent");
  const [libraryLoaderFilter, setLibraryLoaderFilter] = useState("");
  const [expandedInstance, setExpandedInstance] = useState<string | null>(null);

  useEffect(() => { void loadInstances(); }, [loadInstances]);
  useEffect(() => { if (modal && !versions.length) void refreshVersions(); }, [modal, versions.length, refreshVersions]);
  useEffect(() => { if (modal && !javaInstallations.length) void scanJava(); }, [modal, javaInstallations.length, scanJava]);
  useEffect(() => { if (modal && latestRelease && !versionsLoading) setVersion((current) => versions.some((item) => item.id === current) ? current : latestRelease); }, [latestRelease, modal, versions, versionsLoading]);
  useEffect(() => {
    if (!modal || loader === "Vanilla" || !version) { setLoaderVersions([]); setLoaderVersion(""); setLoaderVersionsError(null); return; }
    let cancelled = false;
    const command = { Fabric: "list_fabric_loader_versions", Quilt: "list_quilt_loader_versions", Forge: "list_forge_versions", NeoForge: "list_neoforge_versions" }[loader];
    if (!command) return;
    setLoaderVersionsLoading(true);
    setLoaderVersionsError(null);
    invoke<string[]>(command, { minecraftVersion: version })
      .then((list) => { if (!cancelled) { setLoaderVersions(list); setLoaderVersion(list[0] || ""); } })
      .catch((error) => { if (!cancelled) { setLoaderVersions([]); setLoaderVersion(""); setLoaderVersionsError(String(error).replace("Error: ", "")); } })
      .finally(() => { if (!cancelled) setLoaderVersionsLoading(false); });
    return () => { cancelled = true; };
  }, [modal, loader, version]);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!name.trim()) return;
    await createInstance({ name: name.trim(), minecraftVersion: version, loader, loaderVersion: loader !== "Vanilla" ? loaderVersion || undefined : undefined, gameDirectory: gameDirectory.trim() || undefined });
    setName(""); setGameDirectory(""); setModal(false);
  };
  const duplicate = async (instance: Instance) => {
    await createInstance({ name: `${instance.name} Copy`, minecraftVersion: instance.minecraftVersion, loader: instance.loader, loaderVersion: instance.loaderVersion });
  };
  const formatSize = (bytes: number) => bytes < 1024 ? `${bytes} B` : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  const ensureJavaFor = async (minecraftVersion: string): Promise<string> => {
    const requiredMajor = JavaRuntime.requiredFor(minecraftVersion);
    // If Java has never been scanned in this session, a previously
    // downloaded shared runtime could already be sitting on disk without
    // the store knowing about it yet -- scan first so we don't re-download
    // something that's already there.
    if (!javaInstallations.length) { await scanJava(); }
    const installs = useJavaStore.getState().javaInstallations;
    const currentSelection = useJavaStore.getState().selectedJava;
    // Prefer the user's own selection if it already satisfies this instance;
    // otherwise look for any already-known install (manual or previously
    // downloaded managed runtime) before downloading a new one.
    const compatible = (currentSelection && javaMajor(currentSelection.version) === requiredMajor)
      ? currentSelection
      : installs.find((item) => javaMajor(item.version) === requiredMajor);
    if (compatible) { useJavaStore.getState().setDefaultJava(compatible); return compatible.path; }

    // No matching runtime anywhere -- download Java {requiredMajor} once into
    // the shared <data>/runtime folder (see scan_java/download_java on the
    // Rust side). Every instance that needs that major version reuses the
    // same install from then on instead of each build fetching its own copy.
    setLaunchStatus(`Downloading Java ${requiredMajor} (shared runtime, one-time)…`);
    const java = await useJavaStore.getState().downloadJava(requiredMajor);
    return java.path;
  };

  const launch = async (instance: Instance, accountOverride?: Account) => {
    const account = accountOverride ?? selectedAccount;
    setLaunchError(null);
    setLaunching(instance.name);
    const gameDirectory = instance.gameDirectory || `${instance.name}/`;
    try {
      // ── Resolve the version URL and ID based on the loader ──────────────────
      let versionUrl: string;
      let resolvedVersion: string;

      const loader = instance.loader ?? "Vanilla";
      const javaPath = await ensureJavaFor(instance.minecraftVersion);
      // Set when the loader's own installer already produced a fully playable
      // instance directory — skips the generic DownloadQueue.download() step below.
      let skipDownload = false;

      if (loader === "Fabric") {
        setLaunchStatus("Resolving Fabric loader…");
        const info = await invoke<{ id: string; url: string; loaderVersion: string }>("get_fabric_loader_url", { minecraftVersion: instance.minecraftVersion, loaderVersion: instance.loaderVersion });
        versionUrl = info.url;
        resolvedVersion = info.id;
      } else if (loader === "Quilt") {
        setLaunchStatus("Resolving Quilt loader…");
        const info = await invoke<{ id: string; url: string; loaderVersion: string }>("get_quilt_loader_url", { minecraftVersion: instance.minecraftVersion, loaderVersion: instance.loaderVersion });
        versionUrl = info.url;
        resolvedVersion = info.id;
      } else if (loader === "Forge") {
        setLaunchStatus("Installing Forge…");
        const versionId = await invoke<string>("install_forge", {
          javaPath,
          minecraftVersion: instance.minecraftVersion,
          instanceDirectory: gameDirectory,
          forgeVersion: instance.loaderVersion,
        });
        resolvedVersion = versionId;
        versionUrl = "";
        skipDownload = true;
      } else if (loader === "NeoForge") {
        setLaunchStatus("Installing NeoForge…");
        const versionId = await invoke<string>("install_neoforge", {
          javaPath,
          minecraftVersion: instance.minecraftVersion,
          instanceDirectory: gameDirectory,
          neoforgeVersion: instance.loaderVersion,
        });
        resolvedVersion = versionId;
        versionUrl = "";
        skipDownload = true;
      } else {
        // Vanilla (and Forge/NeoForge for now — download vanilla, run with it)
        setLaunchStatus("Checking version files…");
        const manifestEntry = versions.find((item) => item.id === instance.minecraftVersion);
        if (!manifestEntry) { await refreshVersions(); }
        const entry = manifestEntry ?? versions.find((item) => item.id === instance.minecraftVersion);
        if (!entry) throw new Error(`Could not find Mojang manifest entry for ${instance.minecraftVersion}.`);
        versionUrl = entry.url;
        resolvedVersion = instance.minecraftVersion;
      }

      if (!skipDownload) {
        setLaunchStatus(`Downloading ${loader === "Vanilla" ? "Minecraft" : loader} files…`);
        const task: DownloadTask = {
          id: `${instance.name}-${Date.now()}`,
          version: resolvedVersion,
          status: "downloading",
          progress: 0,
          downloadedBytes: 0,
          totalBytes: 0,
        };
        await DownloadQueue.download(task, versionUrl, gameDirectory);
      }

      setLaunchStatus("Starting Minecraft…");
      const effectiveRamMax = MemoryManager.clamp(ramMax, memory.systemMemoryMb);
      const extraJvmArguments = javaArgs.trim() ? javaArgs.trim().split(/\s+/) : undefined;

      const result = await InstanceManager.launch({
        instanceDirectory: gameDirectory,
        version: resolvedVersion,
        javaPath,
        ramMin,
        ramMax: effectiveRamMax,
        width,
        height,
        username: account?.username,
        uuid: account?.uuid,
        userType: account?.type === "Ely.by" ? "ely" : "legacy",
        accessToken: account?.type === "Ely.by" ? account.accessToken : undefined,
        extraJvmArguments,
      });

      setLaunchStatus(`Launched (pid ${result.pid}).`);

      const logStore = useLogsStore.getState();
      logStore.startSession(result.pid, instance.name, resolvedVersion);
      // Emit launcher-level diagnostic lines — visible via the Logs page toggle.
      logStore.appendLine(result.pid, "launcher", `[Launcher] Instance  : ${instance.name}`);
      logStore.appendLine(result.pid, "launcher", `[Launcher] Minecraft : ${instance.minecraftVersion}  Loader: ${loader}`);
      logStore.appendLine(result.pid, "launcher", `[Launcher] Version ID: ${resolvedVersion}`);
      logStore.appendLine(result.pid, "launcher", `[Launcher] Java      : ${javaPath}`);
      logStore.appendLine(result.pid, "launcher", `[Launcher] RAM       : ${ramMin}M – ${effectiveRamMax}M`);
      logStore.appendLine(result.pid, "launcher", `[Launcher] Directory : ${gameDirectory}`);
      logStore.appendLine(result.pid, "launcher", `[Launcher] PID       : ${result.pid}`);
    } catch (reason) {
      setLaunchError(reason instanceof Error ? reason.message : String(reason));
      setLaunchStatus(null);
    } finally {
      setLaunching(null);
      setPlayWithFor(null);
    }
  };

  // "View"/"Edit" -- both read the same content summary; Edit just adds
  // interactive add/remove buttons on top of the same lists.
  const openContent = async (instance: Instance, mode: "view" | "edit") => {
    setContentModal({ name: instance.name, mode });
    setContentSummary(null);
    setContentError(null);
    setContentLoading(true);
    try {
      setContentSummary(await InstanceContentService.get(instance.name));
    } catch (loadError) {
      setContentError(loadError instanceof Error ? loadError.message : String(loadError));
    } finally {
      setContentLoading(false);
    }
  };
  const refreshContent = async () => {
    if (!contentModal) return;
    try { setContentSummary(await InstanceContentService.get(contentModal.name)); } catch { /* keep last known content on refresh failure */ }
  };
  const removeContentFile = async (category: "mod" | "resourcepack" | "shader", filename: string) => {
    if (!contentModal) return;
    setContentError(null);
    try {
      await InstanceContentService.removeFile(contentModal.name, category, filename);
      await refreshContent();
    } catch (removeError) {
      setContentError(removeError instanceof Error ? removeError.message : String(removeError));
    }
  };
  const addContentFile = async (category: "mod" | "resourcepack" | "shader") => {
    if (!contentModal) return;
    setContentError(null);
    try {
      const path = await InstanceContentService.browseLocalFile();
      if (!path) return;
      setAddingCategory(category);
      await InstanceContentService.addFile(contentModal.name, category, path);
      await refreshContent();
    } catch (addError) {
      setContentError(addError instanceof Error ? addError.message : String(addError));
    } finally {
      setAddingCategory(null);
    }
  };
  const availableVersions = versions.filter((item) => (versionFilter === "all" || item.type === versionFilter) && item.id.toLowerCase().includes(versionSearch.toLowerCase()));
  const libraryLoaders = Array.from(new Set(instances.map((item) => item.loader))).sort();
  const visibleInstances = instances
    .filter((item) => !libraryLoaderFilter || item.loader === libraryLoaderFilter)
    .filter((item) => !librarySearch.trim() || item.name.toLowerCase().includes(librarySearch.trim().toLowerCase()) || item.minecraftVersion.toLowerCase().includes(librarySearch.trim().toLowerCase()))
    .sort((a, b) => {
      if (librarySort === "name") return a.name.localeCompare(b.name);
      if (librarySort === "size") return b.size - a.size;
      return new Date(b.created).getTime() - new Date(a.created).getTime();
    });
  const buildLaunchCommand = (instance: Instance) => {
    const profile: LaunchProfile = {
      instanceName: instance.name,
      minecraftVersion: instance.minecraftVersion,
      gameDirectory: instance.gameDirectory || `${instance.name}/`,
      javaPath: selectedJava?.path || "java",
      ramMin,
      ramMax: MemoryManager.clamp(ramMax, memory.systemMemoryMb),
      resolutionWidth: width,
      resolutionHeight: height,
      jvmArguments: javaArgs.trim() ? javaArgs.trim().split(/\s+/) : [],
      gameArguments: [`--width=${width}`, `--height=${height}`],
      username: selectedAccount?.username,
      uuid: selectedAccount?.uuid,
      userType: selectedAccount?.type === "Ely.by" ? "ely" : "legacy",
      accessToken: selectedAccount?.type === "Ely.by" ? selectedAccount.accessToken : undefined,
    };
    setLaunchCommand(LauncherService.buildCommand(profile));
  };

  return <div className="instances-page">
    <div className="page-title instances-header"><div><span className="eyebrow"><Blocks size={13} /> LIBRARY</span><h1>Instances</h1><p>Your Minecraft installations, worlds, and configurations.</p></div><button className="primary-button" onClick={() => setModal(true)}><Plus size={16} /> Create Instance</button></div>
    {error && <div className="instance-error">{error.replace("Error: ", "")}</div>}
    {launchError && <div className="instance-error">{launchError.replace("Error: ", "")}</div>}
    {launchStatus && <div className="instance-meta launch-status-banner">{launchStatus}</div>}
    {instances.length > 0 && <div className="instances-toolbar">
      <div className="instances-search"><Search size={14} /><input value={librarySearch} onChange={(event) => setLibrarySearch(event.target.value)} placeholder="Search instances…" /></div>
      {libraryLoaders.length > 1 && <select value={libraryLoaderFilter} onChange={(event) => setLibraryLoaderFilter(event.target.value)}><option value="">All loaders</option>{libraryLoaders.map((item) => <option key={item} value={item}>{item}</option>)}</select>}
      <select value={librarySort} onChange={(event) => setLibrarySort(event.target.value as typeof librarySort)}>
        <option value="recent">Newest first</option>
        <option value="name">Name (A–Z)</option>
        <option value="size">Size (largest first)</option>
      </select>
      <span className="instances-count">{visibleInstances.length} of {instances.length}</span>
    </div>}
    {loading && !instances.length ? <div className="instance-loading">Loading your instances…</div> : instances.length ? (visibleInstances.length ? <div className="instances-list">{visibleInstances.map((instance) => {
      const isExpanded = expandedInstance === instance.name;
      return <article className={`managed-instance-card${isExpanded ? " expanded" : " collapsed"}`} key={instance.name} onClick={() => { selectInstance(instance); setExpandedInstance(instance.name); }}>
      <div className="managed-banner"><div className="instance-mark">{instance.iconPath ? <img src={convertFileSrc(instance.iconPath)} alt="" /> : <Blocks size={25} />}</div><button className="card-menu" aria-label={`Actions for ${instance.name}`}><MoreHorizontal size={18} /></button></div>
      <div className="managed-body"><div className="managed-heading"><div><h2>{instance.name}</h2><p>{instance.minecraftVersion} <span>·</span> {instance.loader}{instance.loaderVersion && instance.loader !== "Vanilla" ? ` ${instance.loaderVersion}` : ""}</p></div><span className="status-pill">READY</span></div>
      {!isExpanded && <div className="instance-actions collapsed-actions">
        <button className="primary-button launch-button" disabled={launching === instance.name} onClick={(event) => { event.stopPropagation(); void launch(instance); }}>{launching === instance.name ? <LoaderCircle className="spin" size={14} /> : <Rocket size={14} />} {launching === instance.name ? "Launching…" : "Play"}</button>
      </div>}
      <div className={`instance-expand${isExpanded ? " open" : ""}`}><div>
      <div className="instance-meta"><span>Created {new Date(instance.created).toLocaleDateString()}</span><span>{formatSize(instance.size)}</span></div><div className="instance-actions">
        <button className="primary-button launch-button" disabled={launching === instance.name} onClick={(event) => { event.stopPropagation(); void launch(instance); }}>{launching === instance.name ? <LoaderCircle className="spin" size={14} /> : <Rocket size={14} />} {launching === instance.name ? "Launching…" : "Play"}</button>
        <div className="play-with-wrap">
          <button title="Play with…" onClick={(event) => { event.stopPropagation(); setPlayWithFor(playWithFor === instance.name ? null : instance.name); }}><UserRound size={14} /></button>
          {playWithFor === instance.name && <div className="play-with-menu" onClick={(event) => event.stopPropagation()}>
            {accounts.length
              ? accounts.map((account) => <button key={account.id} onClick={() => void launch(instance, account)} disabled={launching === instance.name}>{account.username}</button>)
              : <span className="play-with-empty">No saved accounts</span>}
          </div>}
        </div>
        <button title="View" onClick={(event) => { event.stopPropagation(); void openContent(instance, "view"); }}><Eye size={14} /></button>
        <button title="Edit (mods/resourcepacks)" onClick={(event) => { event.stopPropagation(); void openContent(instance, "edit"); }}><SlidersHorizontal size={14} /></button>
        <button title="Rename" onClick={(event) => { event.stopPropagation(); setEditing(instance.name); setNewName(instance.name); }}><Pencil size={14} /></button>
        <button title="Duplicate" onClick={(event) => { event.stopPropagation(); void duplicate(instance); }}><Copy size={14} /></button>
        <button title="Open folder" onClick={(event) => { event.stopPropagation(); void InstanceManager.openFolder(instance.name); }}><FolderOpen size={14} /></button>
        <button className="danger-action" title="Delete" onClick={(event) => { event.stopPropagation(); if (window.confirm(`Delete ${instance.name}?`)) void deleteInstance(instance.name); }}><Trash2 size={14} /></button>
      </div>
      </div></div>
      </div>
     </article>;
    })}</div> : <div className="instances-empty-filtered"><Search size={20} /><span>No instances match your search.</span></div>) : <div className="instances-empty"><div className="empty-icon"><Blocks size={24} /></div><span className="eyebrow">YOUR LIBRARY IS READY</span><h2>No instances yet</h2><p>Create your first Minecraft instance to get started.</p><button className="primary-button" onClick={() => setModal(true)}><Plus size={15} /> Create Instance</button></div>}
     {instances.length > 0 && <section className="launch-debug-builder"><div className="section-heading"><div><span className="eyebrow"><Terminal size={13} /> LAUNCH CORE</span><h2>Build Launch Command</h2><p className="section-description">Prepare a launch command for the selected instance. Minecraft will not start yet.</p></div></div><div className="launch-form-grid"><label>Java<select value={selectedJava?.path || ""} onChange={(event) => { const java = javaInstallations.find((item) => item.path === event.target.value); if (java) useJavaStore.getState().setDefaultJava(java); }}><option value="">System Java</option>{javaInstallations.map((java) => <option key={java.path} value={java.path}>{java.version} · {java.vendor}</option>)}</select></label><label>RAM Minimum (MB)<input type="number" min={512} max={memory.maximumMemoryMb} value={ramMin} onChange={(event) => setRamMin(Number(event.target.value))} /></label><label>RAM Maximum (MB)<input type="number" min={512} max={memory.maximumMemoryMb} value={ramMax} onChange={(event) => setRamMax(Number(event.target.value))} /></label><label>Width<input type="number" min={640} value={width} onChange={(event) => setWidth(Number(event.target.value))} /></label><label>Height<input type="number" min={480} value={height} onChange={(event) => setHeight(Number(event.target.value))} /></label><label className="full-row">Java Arguments<input value={javaArgs} onChange={(event) => setJavaArgs(event.target.value)} placeholder="-XX:+UseZGC -Dfoo=bar" /></label></div><div className="memory-note">System RAM: {memory.systemMemoryMb} MB · Maximum allowed: {memory.maximumMemoryMb} MB · Recommended: {memory.recommendedMemoryMb} MB</div><button className="primary-button" onClick={() => buildLaunchCommand(instances[0])}><Terminal size={15} /> Build Launch Command</button></section>}
     {launchCommand && <section className="launch-debug-panel"><div className="section-heading"><div><span className="eyebrow">LAUNCH DEBUG</span><h2>Command prepared</h2></div><button className="text-button" onClick={() => setLaunchCommand(null)}>Clear</button></div><div className="debug-grid"><div><span>Java</span><strong>{launchCommand.javaPath}</strong></div><div><span>RAM</span><strong>{launchCommand.ram}</strong></div><div><span>Version</span><strong>{launchCommand.version}</strong></div><div><span>Working Directory</span><strong>{launchCommand.workingDirectory}</strong></div><div><span>Libraries</span><strong>{launchCommand.libraries.length ? launchCommand.libraries.length : "Not downloaded yet"}</strong></div></div><pre className="command-output">{launchCommand.command}</pre></section>}
    {contentModal && <div className="modal-backdrop" onClick={() => setContentModal(null)}><div className="modal-card compact-modal" onClick={(event) => event.stopPropagation()}>
      <button className="modal-close" onClick={() => setContentModal(null)}><X size={17} /></button>
      <span className="eyebrow">{contentModal.mode === "edit" ? "EDIT INSTANCE CONTENT" : "INSTANCE CONTENT"}</span>
      <h2>{contentModal.name}</h2>
      {contentLoading && <div className="data-dir-note"><LoaderCircle className="spin" size={13} /> Loading...</div>}
      {contentError && <div className="java-error">{contentError}</div>}
      {contentSummary && <div className="instance-content-sections">
        {([["mods", "Mods", "mod"], ["resourcepacks", "Resource Packs", "resourcepack"], ["shaderpacks", "Shader Packs", "shader"]] as const).map(([key, label, category]) => <div className="instance-content-section" key={key}>
          <div className="instance-content-section-head">
            <strong>{label} ({contentSummary[key].length})</strong>
            {contentModal.mode === "edit" && <button className="secondary-button" onClick={() => void addContentFile(category)} disabled={addingCategory === category}>{addingCategory === category ? <LoaderCircle className="spin" size={12} /> : <Upload size={12} />} Add...</button>}
          </div>
          {contentSummary[key].length
            ? <ul className="theme-maker-font-list">{contentSummary[key].map((filename) => <li key={filename}>
                <span>{filename}</span>
                {contentModal.mode === "edit" && <button className="icon-action" title="Remove" onClick={() => void removeContentFile(category, filename)}><Trash2 size={13} /></button>}
              </li>)}</ul>
            : <p className="data-dir-note">None yet.</p>}
        </div>)}
        <div className="instance-content-section">
          <strong>Worlds ({contentSummary.worlds.length})</strong>
          {contentSummary.worlds.length ? <p className="data-dir-note">{contentSummary.worlds.join(", ")}</p> : <p className="data-dir-note">None yet.</p>}
        </div>
        <div className="instance-content-section">
          <strong>Screenshots ({contentSummary.screenshots.length})</strong>
        </div>
      </div>}
    </div></div>}

    {editing && <div className="modal-backdrop" onClick={() => setEditing(null)}><div className="modal-card compact-modal" onClick={(event) => event.stopPropagation()}><button className="modal-close" onClick={() => setEditing(null)}><X size={17} /></button><span className="eyebrow">INSTANCE SETTINGS</span><h2>Rename instance</h2><input autoFocus value={newName} onChange={(event) => setNewName(event.target.value)} /><button className="primary-button modal-submit" onClick={async () => { if (newName.trim()) { await renameInstance(editing, newName.trim()); setEditing(null); } }}>Save name</button></div></div>}
    {modal && <div className="modal-backdrop" onClick={() => setModal(false)}><form className="modal-card version-modal" onSubmit={submit} onClick={(event) => event.stopPropagation()}><button type="button" className="modal-close" onClick={() => setModal(false)}><X size={17} /></button><span className="eyebrow">NEW INSTANCE</span><h2>Create Instance</h2><label>Name<input value={name} onChange={(event) => setName(event.target.value)} placeholder="My Survival World" required /></label><div className="version-label-row"><label>Minecraft Version{versionsLoading ? <div className="version-loading"><LoaderCircle className="spin" size={14} /> Loading Mojang versions…</div> : <select value={version} onChange={(event) => setVersion(event.target.value)} required><option value="" disabled>Select a version</option>{availableVersions.map((item) => <option key={item.id} value={item.id}>{item.id} · {item.type} · {new Date(item.releaseTime).toLocaleDateString()}</option>)}</select>}</label><button type="button" className="refresh-versions" onClick={() => void refreshVersions()} title="Refresh versions"><LoaderCircle className={versionsLoading ? "spin" : ""} size={15} /></button></div>{versionsError && <div className="version-error">{versionsError}</div>}<div className="version-tools"><div className="version-search"><Search size={14} /><input value={versionSearch} onChange={(event) => setVersionSearch(event.target.value)} placeholder="Search versions" /></div><div className="version-filters">{[["all", "All"], ["release", "Release"], ["snapshot", "Snapshot"], ["old_alpha", "Old Alpha"], ["old_beta", "Old Beta"]].map(([value, label]) => <button type="button" className={versionFilter === value ? "active" : ""} key={value} onClick={() => setVersionFilter(value as typeof versionFilter)}>{label}{value === "release" && latestRelease && <Check size={11} />}</button>)}</div></div><label>Loader<select value={loader} onChange={(event) => setLoader(event.target.value)}>{loaders.map((item) => <option key={item}>{item}</option>)}</select></label>{loader !== "Vanilla" && <label>Loader Version{loaderVersionsLoading ? <div className="version-loading"><LoaderCircle className="spin" size={14} /> Loading {loader} versions…</div> : <select value={loaderVersion} onChange={(event) => setLoaderVersion(event.target.value)} required>{!loaderVersions.length && <option value="" disabled>No versions found</option>}{loaderVersions.map((item) => <option key={item} value={item}>{item}</option>)}</select>}</label>}{loaderVersionsError && <div className="version-error">{loaderVersionsError}</div>}<label>Game Directory<input value={gameDirectory} onChange={(event) => setGameDirectory(event.target.value)} placeholder="Optional custom directory" /></label><button className="primary-button modal-submit" type="submit" disabled={!version || versionsLoading}><Plus size={15} /> Create Instance</button></form></div>}
  </div>;
}