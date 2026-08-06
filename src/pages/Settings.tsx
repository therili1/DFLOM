import { useEffect, useRef, useState } from "react";
import { convertFileSrc, invoke } from "@tauri-apps/api/core";
import { getCurrentWebview } from "@tauri-apps/api/webview";
import { Check, Cpu, Download, FolderOpen, HardDrive, LoaderCircle, Moon, Palette, RotateCcw, Search, Sparkles, Sun, Trash2, Upload } from "lucide-react";
import { JavaManager } from "../services/java/JavaManager";
import { useJavaStore } from "../stores/javaStore";
import { useThemeStore } from "../stores/themeStore";
import { useAccentStore, ACCENT_PRESETS } from "../stores/accentStore";
import { useThemeEngineStore } from "../stores/themeEngineStore";
import { useDataDirectoryStore } from "../stores/dataDirectoryStore";
import { JavaRuntime } from "../services/java/JavaRuntime";
import { MemoryManager } from "../services/launcher/MemoryManager";

export default function Settings() {
  const { isDark, toggleTheme } = useThemeStore();
  const { accent, setAccent } = useAccentStore();
  const { javaInstallations, selectedJava, loading, error, scanJava, addJava, removeJava, setDefaultJava, downloadJava } = useJavaStore();
  const { path: dataPath, loading: dataDirLoading, error: dataDirError, load: loadDataDirectory, browseAndSet: browseDataDirectory, reset: resetDataDirectory } = useDataDirectoryStore();
  const { themes: themeEnginePacks, loading: themeEngineLoading, installing: themeEngineInstalling, error: themeEngineError, loadThemes: loadThemeEnginePacks, installTheme, activateTheme: activateThemePack, removeTheme: removeThemePack } = useThemeEngineStore();
  const [minecraftVersion, setMinecraftVersion] = useState("1.21");
  const memory = MemoryManager.getInfo();
  const [themeDropOver, setThemeDropOver] = useState(false);
  const themeDropZoneRef = useRef<HTMLDivElement>(null);
  const [geminiKeyInput, setGeminiKeyInput] = useState("");
  const [geminiKeySaved, setGeminiKeySaved] = useState(false);
  const [geminiKeySaving, setGeminiKeySaving] = useState(false);
  const [geminiKeyError, setGeminiKeyError] = useState<string | null>(null);
  useEffect(() => {
    invoke<boolean>("has_gemini_api_key").then(setGeminiKeySaved).catch(() => {});
  }, []);

  const saveGeminiKey = async () => {
    setGeminiKeyError(null);
    setGeminiKeySaving(true);
    try {
      await invoke("save_gemini_api_key", { apiKey: geminiKeyInput.trim() });
      setGeminiKeySaved(true);
      setGeminiKeyInput("");
    } catch (saveError) {
      setGeminiKeyError(saveError instanceof Error ? saveError.message : String(saveError));
    } finally {
      setGeminiKeySaving(false);
    }
  };

  const clearGeminiKey = async () => {
    setGeminiKeyError(null);
    try {
      await invoke("save_gemini_api_key", { apiKey: "" });
      setGeminiKeySaved(false);
    } catch (clearError) {
      setGeminiKeyError(clearError instanceof Error ? clearError.message : String(clearError));
    }
  };


  // Native drag&drop for installing a .dftp theme pack by dropping it onto
  // the Theme Packs section, instead of only through the file picker.
  //
  // WARNING: NOT verified against a real Tauri 2 build. Mirrors the same
  // onDragDropEvent pattern already used (and left with the same caveat)
  // in ThemeMaker.tsx for the background/video dropzone.
  useEffect(() => {
    let unlisten: (() => void) | undefined;
    let cancelled = false;

    (async () => {
      try {
        const webview = getCurrentWebview();
        const dispose = await webview.onDragDropEvent((event) => {
          const payload = event.payload as { type: string; paths?: string[]; position?: { x: number; y: number } };

          if (payload.type === "over") {
            const rect = themeDropZoneRef.current?.getBoundingClientRect();
            const pos = payload.position;
            const inside = !!rect && !!pos && pos.x >= rect.left && pos.x <= rect.right && pos.y >= rect.top && pos.y <= rect.bottom;
            setThemeDropOver(inside);
            return;
          }
          if (payload.type === "leave") {
            setThemeDropOver(false);
            return;
          }
          if (payload.type === "drop") {
            setThemeDropOver(false);
            const rect = themeDropZoneRef.current?.getBoundingClientRect();
            const pos = payload.position;
            const droppedInsideZone = !!rect && !!pos && pos.x >= rect.left && pos.x <= rect.right && pos.y >= rect.top && pos.y <= rect.bottom;
            if (!droppedInsideZone) return;

            const path = payload.paths?.[0];
            if (!path || !path.toLowerCase().endsWith(".dftp")) return;
            void installTheme(path);
          }
        });
        if (!cancelled) unlisten = dispose; else dispose();
      } catch (dragDropError) {
        console.warn("Drag & drop unavailable:", dragDropError);
      }
    })();

    return () => { cancelled = true; unlisten?.(); };
  }, [installTheme]);
  const [ram, setRam] = useState(memory.recommendedMemoryMb);
  useEffect(() => { void scanJava(); }, [scanJava]);
  useEffect(() => { void loadDataDirectory(); }, [loadDataDirectory]);
  useEffect(() => { void loadThemeEnginePacks(); }, [loadThemeEnginePacks]);
  const browse = async () => { const path = await JavaManager.browse(); if (path) await addJava(path); };
  return <div className="settings-page"><div className="page-title"><span className="eyebrow">PREFERENCES</span><h1>Settings</h1><p>Configure Dream Future Launcher for your machine.</p></div>
    <section className="settings-card"><div className="setting-row"><div className="setting-icon"><HardDrive size={18} /></div><div style={{ flex: 1 }}><h3>Storage location</h3><p>Where instances, themes, Java runtimes, and accounts are stored. Defaults to your system's AppData/Local folder.</p>
      <div className="data-dir-row">
        <code className="data-dir-path">{dataDirLoading && !dataPath ? "Loading..." : dataPath}</code>
        <button className="secondary-button" onClick={() => void browseDataDirectory()} disabled={dataDirLoading}>{dataDirLoading ? <LoaderCircle className="spin" size={14} /> : <FolderOpen size={14} />} Change...</button>
        <button className="icon-action" title="Reset to default (AppData/Local)" onClick={() => void resetDataDirectory()} disabled={dataDirLoading}><RotateCcw size={15} /></button>
      </div>
      <p className="data-dir-note">Note: changing this only affects new data going forward — existing instances/themes/Java installs already on disk are not moved automatically.</p>
      {dataDirError && <div className="java-error">{dataDirError}</div>}
    </div></div></section>

    <section className="settings-card"><div className="setting-row"><div className="setting-icon"><Sparkles size={18} /></div><div style={{ flex: 1 }}><h3>AI assistant (Google AI Studio)</h3><p>Powers the "Generate with AI" button in Theme Maker. Bring your own free API key from <a href="https://aistudio.google.com/apikey" target="_blank" rel="noreferrer">aistudio.google.com/apikey</a> — it's stored locally and used only to call Google's API directly.</p>
      <div className="data-dir-row">
        <input type="password" placeholder={geminiKeySaved ? "•••••••••••••••• (saved)" : "Paste your Gemini API key"} value={geminiKeyInput} onChange={(event) => setGeminiKeyInput(event.target.value)} style={{ flex: 1 }} />
        <button className="secondary-button" onClick={() => void saveGeminiKey()} disabled={geminiKeySaving || !geminiKeyInput.trim()}>{geminiKeySaving ? <LoaderCircle className="spin" size={14} /> : <Check size={14} />} Save</button>
        {geminiKeySaved && <button className="icon-action" title="Remove saved key" onClick={() => void clearGeminiKey()}><Trash2 size={15} /></button>}
      </div>
      {geminiKeyError && <div className="java-error">{geminiKeyError}</div>}
    </div></div></section>

    <section className="settings-card"><div className="setting-row"><div className="setting-icon"><Sun size={18} /></div><div><h3>Appearance</h3><p>Switch between a dark and light launcher theme.</p></div><button className="toggle" onClick={toggleTheme} aria-label="Toggle theme">{isDark ? <Moon size={15} /> : <Sun size={15} />}</button></div></section>

    <section className="settings-card"><div className="setting-row"><div className="setting-icon"><Palette size={18} /></div><div style={{ flex: 1 }}><h3>Accent color</h3><p>Choose the launcher's primary highlight color.</p>
      <div className="accent-picker">
        {ACCENT_PRESETS.map((preset) => (
          <button key={preset.color} className={`accent-swatch ${accent.toLowerCase() === preset.color.toLowerCase() ? "active" : ""}`} style={{ background: preset.color }} title={preset.name} aria-label={preset.name} onClick={() => setAccent(preset.color)} />
        ))}
        <div className="accent-custom">
          <input type="color" value={accent} onChange={(event) => setAccent(event.target.value)} title="Custom accent color" />
          <span>{accent.toUpperCase()}</span>
        </div>
      </div>
    </div></div></section>

    <section ref={themeDropZoneRef} className={`theme-section ${themeDropOver ? "theme-section--drag-over" : ""}`}><div className="section-heading"><div><span className="eyebrow"><Palette size={13} /> THEME ENGINE</span><h2>Theme Packs (.dftp)</h2><p className="section-description">Install packaged .dftp theme packs — background, icons, and fonts bundled together. You can also drag a .dftp file here.</p></div><div className="java-toolbar"><button className="primary-button" onClick={() => void installTheme()} disabled={themeEngineInstalling}>{themeEngineInstalling ? <LoaderCircle className="spin" size={14} /> : <Upload size={14} />} Install .dftp</button></div></div>
      {themeEngineError && <div className="java-error">{themeEngineError}</div>}
      <div className="theme-list">
        {themeEnginePacks.map((pack) => <article className={`theme-card ${pack.isActive ? "default-java" : ""}`} key={pack.id}>
          {pack.previewPath
            ? <img className="theme-pack-preview" src={convertFileSrc(pack.previewPath)} alt={`${pack.name} preview`} loading="lazy" />
            : <div className="theme-pack-preview theme-pack-preview--placeholder"><Palette size={20} /></div>}
          <div className="theme-card-info"><h3>{pack.name}</h3><code>{pack.author} · v{pack.version}{pack.hasCustomCss ? " · CSS" : ""}</code></div>
          <div className="java-actions">
            {pack.isActive ? <span className="default-pill"><Check size={11} /> ACTIVE</span> : <button onClick={() => void activateThemePack(pack.id)}>Use</button>}
            <button className="icon-action" title="Delete theme pack" onClick={() => void removeThemePack(pack.id)}><Trash2 size={15} /></button>
          </div>
        </article>)}
        {!themeEnginePacks.length && !themeEngineLoading && <div className="java-empty"><Palette size={23} /><h3>No theme packs installed</h3><p>Install a .dftp file to fully restyle the launcher, background included, or drag one into this section.</p></div>}
      </div>
    </section>

    <section className="java-section"><div className="section-heading"><div><span className="eyebrow"><Cpu size={13} /> RUNTIMES</span><h2>Java Manager</h2><p className="section-description">Manage Java installations used by Minecraft instances. Downloaded runtimes are shared — one install per major version, reused by every instance instead of downloading per build.</p></div><div className="java-toolbar"><button className="secondary-button" onClick={() => void browse()}><FolderOpen size={14} /> Add manually</button><button className="primary-button" onClick={() => void scanJava()} disabled={loading}>{loading ? <LoaderCircle className="spin" size={14} /> : <Search size={14} />} Scan</button></div></div>
      {error && <div className="java-error">{error.replace("Error: ", "")}</div>}
      {!loading && !javaInstallations.length && !error && <div className="java-empty"><Cpu size={23} /><h3>No Java installations found</h3><p>Scan your computer or add a java executable manually.</p></div>}
       <div className="java-context"><label>Minecraft version<input value={minecraftVersion} onChange={(event) => setMinecraftVersion(event.target.value)} /></label><span>Required Java: <strong>Java {JavaRuntime.requiredFor(minecraftVersion)}</strong></span></div><div className="java-list">{javaInstallations.map((java) => { const compatible = JavaRuntime.compatible(java, minecraftVersion); return <article className={`java-card ${selectedJava?.path === java.path ? "default-java" : ""}`} key={java.path}><div className="java-icon"><Cpu size={20} /></div><div className="java-info"><div className="java-title"><h3>{java.vendor}</h3>{selectedJava?.path === java.path && <span className="default-pill"><Check size={11} /> DEFAULT</span>}<span className={`compatibility ${compatible ? "compatible" : "incompatible"}`}>{compatible ? "Compatible" : "Not compatible"}</span></div><p>{java.version} · {java.arch} · {java.runtime}</p><code>{java.path}</code></div><div className="java-actions">{selectedJava?.path !== java.path && compatible && <button onClick={() => setDefaultJava(java)}>Select</button>}{java.managed && <><button onClick={() => void JavaManager.openFolder(java.path)}><FolderOpen size={13} /></button><button className="icon-action" title="Delete runtime" onClick={() => void JavaManager.deleteRuntime(java.path)}><Trash2 size={15} /></button></>}</div></article>; })}</div>{!javaInstallations.some((java) => JavaRuntime.compatible(java, minecraftVersion)) && <button className="primary-button runtime-download" disabled={loading} onClick={() => void downloadJava(JavaRuntime.requiredFor(minecraftVersion))}><Download size={15} /> Download Java {JavaRuntime.requiredFor(minecraftVersion)}</button>}
     </section><section className="settings-card memory-card"><div className="setting-row"><div className="setting-icon"><Cpu size={18} /></div><div className="memory-setting"><h3>Memory allocation</h3><p>{ram} MB · Maximum {memory.maximumMemoryMb} MB · System {memory.systemMemoryMb} MB</p><input type="range" min="512" max={memory.maximumMemoryMb} step="256" value={ram} onChange={(event) => setRam(Number(event.target.value))} /></div></div></section>
  </div>;
}