import { useEffect, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import { ArrowDown, ArrowUp, Download, FolderOpen, LoaderCircle, Package, Palette, Type, X } from "lucide-react";
import { convertFileSrc } from "@tauri-apps/api/core";
import { getCurrentWebview } from "@tauri-apps/api/webview";
import { ThemeEngineService } from "../services/theme/ThemeEngineService";
import ThemeAiChatPanel from "../components/ThemeAiChatPanel";

const BACKGROUND_EXTENSIONS = ["png", "jpg", "jpeg", "webp", "gif", "mp4", "webm"];

// Known nav tabs, excluding "home" (which can never be hidden or moved --
// enforced on the backend too). Keep this in sync with Sidebar.tsx's items.
const TAB_LABELS: Record<string, string> = {
  instances: "Instances",
  marketplace: "Marketplace",
  downloads: "Downloads",
  "theme-maker": "Theme Maker",
  "theme-editor": "Theme Editor",
  "ai-helper": "AI Helper",
  logs: "Logs",
  settings: "Settings",
  accounts: "Accounts",
};
const DEFAULT_TAB_ORDER = Object.keys(TAB_LABELS);
// Hybrid Mode's fixed set of per-page CSS slots: the two always-present
// chrome pieces, "home" (not in TAB_LABELS -- it's never hidden/reordered),
// then one slot per nav tab (already includes "ai-helper"). Matches the
// keys sanitize_page_key/theme_pack accept on the backend.
const HYBRID_PAGE_KEYS = ["sidebar", "topbar", "home", ...Object.keys(TAB_LABELS)];
const HYBRID_PAGE_LABELS: Record<string, string> = { sidebar: "Sidebar", topbar: "Topbar", home: "Home", ...TAB_LABELS };
// Same lock as ThemeEditor.tsx / Sidebar.tsx -- a theme that ships with
// these hidden would lock the user out of the only screens that can fix
// the layout, so they can't be hidden even at creation time.
const LOCKED_TABS = new Set(["settings", "theme-editor"]);

const SIDEBAR_POSITIONS = [
  { value: "left", label: "Left" },
  { value: "right", label: "Right" },
  { value: "top", label: "Top" },
  { value: "bottom", label: "Bottom" },
];

export default function ThemeMaker() {
  const [name, setName] = useState("");
  const [author, setAuthor] = useState("");
  const [version, setVersion] = useState("1.0.0");
  const [backgroundPath, setBackgroundPath] = useState<string | null>(null);
  const [previewPath, setPreviewPath] = useState<string | null>(null);
  const [fontPaths, setFontPaths] = useState<string[]>([]);
  const [customCssPath, setCustomCssPath] = useState<string | null>(null);
  const [cssMode, setCssMode] = useState<"standard" | "hybrid">("standard");
  const [pageCssPaths, setPageCssPaths] = useState<Record<string, string>>({});
  const [sidebarPosition, setSidebarPosition] = useState("left");
  const [hiddenTabs, setHiddenTabs] = useState<Set<string>>(new Set());
  const [tabOrder, setTabOrder] = useState<string[]>(DEFAULT_TAB_ORDER);
  const [packing, setPacking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedPath, setSavedPath] = useState<string | null>(null);
  const [templateSaving, setTemplateSaving] = useState(false);
  const [templateSavedPath, setTemplateSavedPath] = useState<string | null>(null);
  const [exampleSaving, setExampleSaving] = useState<"dev" | "video" | null>(null);
  const [exampleSavedPath, setExampleSavedPath] = useState<string | null>(null);
  const [isDragOver, setIsDragOver] = useState(false);
  const dropZoneRef = useRef<HTMLDivElement>(null);
  const [aiHasKey, setAiHasKey] = useState<boolean | null>(null);

  // Runs once on mount AND every time this tab becomes visible again --
  // every page mounts once up front and is just hidden/shown (see
  // MainLayout), so a mount-only check here would miss a key saved in
  // Settings after the app started and leave the input stuck disabled.
  const location = useLocation();
  useEffect(() => {
    if (location.pathname !== "/theme-maker") return;
    ThemeEngineService.hasGeminiApiKey().then(setAiHasKey).catch(() => setAiHasKey(false));
  }, [location.pathname]);

  // Native drag&drop for the background/video dropzone.
  //
  // WARNING: NOT verified against a real Tauri 2 build. Best understanding
  // of the current API -- see prior notes in this file's git history / the
  // project ТЗ for details on what to check if this doesn't work.
  useEffect(() => {
    let unlisten: (() => void) | undefined;
    let cancelled = false;

    (async () => {
      try {
        const webview = getCurrentWebview();
        const dispose = await webview.onDragDropEvent((event) => {
          const payload = event.payload as { type: string; paths?: string[]; position?: { x: number; y: number } };

          if (payload.type === "over") {
            const rect = dropZoneRef.current?.getBoundingClientRect();
            const pos = payload.position;
            const inside = !!rect && !!pos && pos.x >= rect.left && pos.x <= rect.right && pos.y >= rect.top && pos.y <= rect.bottom;
            setIsDragOver(inside);
            return;
          }
          if (payload.type === "leave") {
            setIsDragOver(false);
            return;
          }
          if (payload.type === "drop") {
            setIsDragOver(false);
            const rect = dropZoneRef.current?.getBoundingClientRect();
            const pos = payload.position;
            const droppedInsideZone = !!rect && !!pos && pos.x >= rect.left && pos.x <= rect.right && pos.y >= rect.top && pos.y <= rect.bottom;
            if (!droppedInsideZone) return;

            const path = payload.paths?.[0];
            if (!path) return;
            const ext = path.split(".").pop()?.toLowerCase() ?? "";
            if (!BACKGROUND_EXTENSIONS.includes(ext)) {
              setError(`Unsupported file type for background: .${ext}`);
              return;
            }
            setError(null);
            setBackgroundPath(path);
          }
        });
        if (!cancelled) unlisten = dispose; else dispose();
      } catch (dragDropError) {
        console.warn("Drag & drop unavailable:", dragDropError);
      }
    })();

    return () => { cancelled = true; unlisten?.(); };
  }, []);

  const browseBackground = async () => {
    setError(null);
    try {
      const path = await ThemeEngineService.browseAsset();
      if (path) setBackgroundPath(path);
    } catch (browseError) {
      setError(browseError instanceof Error ? browseError.message : String(browseError));
    }
  };

  const browsePreview = async () => {
    setError(null);
    try {
      const path = await ThemeEngineService.browseAsset();
      if (path) setPreviewPath(path);
    } catch (browseError) {
      setError(browseError instanceof Error ? browseError.message : String(browseError));
    }
  };

  const browseCustomCss = async () => {
    setError(null);
    try {
      const path = await ThemeEngineService.browseCustomCss();
      if (path) setCustomCssPath(path);
    } catch (browseError) {
      setError(browseError instanceof Error ? browseError.message : String(browseError));
    }
  };

  const browsePageCss = async (key: string) => {
    setError(null);
    try {
      const path = await ThemeEngineService.browseCustomCss();
      if (path) setPageCssPaths((current) => ({ ...current, [key]: path }));
    } catch (browseError) {
      setError(browseError instanceof Error ? browseError.message : String(browseError));
    }
  };

  const clearPageCss = (key: string) => setPageCssPaths((current) => {
    const next = { ...current };
    delete next[key];
    return next;
  });

  const addFonts = async () => {
    setError(null);
    try {
      const paths = await ThemeEngineService.browseFonts();
      if (paths.length) setFontPaths((current) => [...new Set([...current, ...paths])]);
    } catch (browseError) {
      setError(browseError instanceof Error ? browseError.message : String(browseError));
    }
  };

  const removeFont = (path: string) => setFontPaths((current) => current.filter((p) => p !== path));

  const downloadTemplate = async () => {
    setError(null);
    setTemplateSaving(true);
    setTemplateSavedPath(null);
    try {
      const path = await ThemeEngineService.downloadTemplate();
      setTemplateSavedPath(path);
    } catch (templateError) {
      setError(templateError instanceof Error ? templateError.message : String(templateError));
    } finally {
      setTemplateSaving(false);
    }
  };

  const downloadExample = async (kind: "dev" | "video") => {
    setError(null);
    setExampleSaving(kind);
    setExampleSavedPath(null);
    try {
      const path = kind === "dev" ? await ThemeEngineService.downloadDevExample() : await ThemeEngineService.downloadVideoExample();
      setExampleSavedPath(path);
    } catch (exampleError) {
      setError(exampleError instanceof Error ? exampleError.message : String(exampleError));
    } finally {
      setExampleSaving(null);
    }
  };

  const toggleTab = (key: string) => {
    if (LOCKED_TABS.has(key)) return;
    setHiddenTabs((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });
  };

  const moveTab = (index: number, direction: -1 | 1) => setTabOrder((current) => {
    const next = [...current];
    const target = index + direction;
    if (target < 0 || target >= next.length) return current;
    [next[index], next[target]] = [next[target], next[index]];
    return next;
  });

  const pack = async () => {
    setError(null);
    setSavedPath(null);
    if (!name.trim() || !author.trim() || !version.trim()) {
      setError("Name, author, and version are all required.");
      return;
    }
    setPacking(true);
    try {
      const path = await ThemeEngineService.pack({
        name, author, version, backgroundPath, previewPath, fontPaths,
        customCssPath: cssMode === "standard" ? customCssPath : null,
        sidebarPosition, hiddenTabs: [...hiddenTabs], tabOrder,
        pageCssPaths: cssMode === "hybrid" ? pageCssPaths : undefined,
      });
      setSavedPath(path);
    } catch (packError) {
      setError(packError instanceof Error ? packError.message : String(packError));
    } finally {
      setPacking(false);
    }
  };

  const backgroundIsVideo = backgroundPath ? ["mp4", "webm"].includes(backgroundPath.split(".").pop()?.toLowerCase() ?? "") : false;

  return <div className="settings-page">
    <div className="page-title" style={{ display: "flex", alignItems: "flex-end", justifyContent: "space-between", gap: 16, flexWrap: "wrap" }}>
      <div><span className="eyebrow">THEME ENGINE</span><h1>Theme Maker</h1><p>Package a background, preview image, fonts, custom CSS, and layout into a shareable .dftp theme file.</p></div>
      <button className="secondary-button" onClick={() => void downloadTemplate()} disabled={templateSaving}>
        {templateSaving ? <LoaderCircle className="spin" size={14} /> : <Download size={14} />} Download theme template
      </button>
    </div>
    {templateSavedPath && <div className="data-dir-note" style={{ marginBottom: 16 }}>Template saved to <code>{templateSavedPath}</code> -- extract it, edit manifest.json/custom.css (fully commented, in Ukrainian), re-zip as .dftp.</div>}

    <div className="data-dir-note" style={{ marginBottom: 16, display: "flex", alignItems: "center", gap: 14, flexWrap: "wrap" }}>
      <span>Reference examples (install via Settings → Theme Packs, not meant to keep):</span>
      <button className="secondary-button" onClick={() => void downloadExample("dev")} disabled={exampleSaving !== null}>
        {exampleSaving === "dev" ? <LoaderCircle className="spin" size={14} /> : <Download size={14} />} Developer Theme Example
      </button>
      <button className="secondary-button" onClick={() => void downloadExample("video")} disabled={exampleSaving !== null}>
        {exampleSaving === "video" ? <LoaderCircle className="spin" size={14} /> : <Download size={14} />} Video Background Example
      </button>
    </div>
    {exampleSavedPath && <div className="data-dir-note" style={{ marginBottom: 16 }}>Saved to <code>{exampleSavedPath}</code>.</div>}

    <section className="settings-card">
      <div className="setting-row" style={{ flexDirection: "column", alignItems: "stretch", gap: 14 }}>
        <label>Name<input value={name} onChange={(event) => setName(event.target.value)} placeholder="My Theme" /></label>
        <label>Author<input value={author} onChange={(event) => setAuthor(event.target.value)} placeholder="Your Name" /></label>
        <label>Version<input value={version} onChange={(event) => setVersion(event.target.value)} placeholder="1.0.0" /></label>

        <div
          ref={dropZoneRef}
          className={`theme-maker-dropzone ${isDragOver ? "theme-maker-dropzone--over" : ""}`}
          onClick={() => void browseBackground()}
        >
          {backgroundPath
            ? (backgroundIsVideo
              ? <video className="theme-pack-preview" style={{ width: 160, height: 90 }} src={convertFileSrc(backgroundPath)} muted loop autoPlay />
              : <img className="theme-pack-preview" style={{ width: 160, height: 90 }} src={convertFileSrc(backgroundPath)} alt="Background" />)
            : <div className="theme-pack-preview theme-pack-preview--placeholder" style={{ width: 160, height: 90 }}><Palette size={24} /></div>}
          <div className="theme-maker-dropzone-text">
            <strong>{backgroundPath ? "Background selected" : "Drag & drop a background image or video"}</strong>
            <span>{backgroundPath ?? "or click to browse (png, jpg, webp, gif, mp4, webm)"}</span>
            {backgroundPath && (
              <span className="theme-maker-bg-type" style={{ display: "inline-flex", gap: 10, marginTop: 4 }}>
                <span style={{ opacity: backgroundIsVideo ? 0.4 : 1 }}>○ Image</span>
                <span style={{ opacity: backgroundIsVideo ? 1 : 0.4 }}>{backgroundIsVideo ? "●" : "○"} Video</span>
              </span>
            )}
          </div>
        </div>

        <div className="data-dir-row">
          <code className="data-dir-path">{previewPath ?? "No preview selected (optional)"}</code>
          <button className="secondary-button" onClick={() => void browsePreview()}><FolderOpen size={14} /> Choose preview...</button>
        </div>
        {previewPath && <img className="theme-pack-preview" style={{ width: 160, height: 90 }} src={convertFileSrc(previewPath)} alt="Preview" />}

        <div className="data-dir-row">
          <code className="data-dir-path">{fontPaths.length ? `${fontPaths.length} font file(s) selected` : "No custom fonts added"}</code>
          <button className="secondary-button" onClick={() => void addFonts()}><Type size={14} /> Add font files...</button>
        </div>
        {fontPaths.length > 0 && <ul className="theme-maker-font-list">
          {fontPaths.map((path) => <li key={path}>
            <span>{path.split(/[/\\]/).pop()}</span>
            <button className="icon-action" title="Remove font" onClick={() => removeFont(path)}><X size={13} /></button>
          </li>)}
        </ul>}
        <p className="data-dir-note">Fonts are bundled into the .dftp as-is. Applying a bundled font automatically when the theme is active is a separate feature, not built yet.</p>

        <div>
          <strong style={{ fontSize: 12 }}>CSS mode</strong>
          <div className="theme-maker-css-mode" style={{ display: "flex", gap: 4, marginTop: 6 }}>
            <button
              className={`secondary-button${cssMode === "standard" ? " active" : ""}`}
              style={{ flex: 1, opacity: cssMode === "standard" ? 1 : 0.6 }}
              onClick={() => setCssMode("standard")}
            >
              Standard Mode
            </button>
            <button
              className={`secondary-button${cssMode === "hybrid" ? " active" : ""}`}
              style={{ flex: 1, opacity: cssMode === "hybrid" ? 1 : 0.6 }}
              onClick={() => setCssMode("hybrid")}
            >
              Hybrid Mode
            </button>
          </div>
          <p className="data-dir-note">
            {cssMode === "standard"
              ? "One custom.css file for the whole theme."
              : "Separate optional CSS files per page/section (sidebar, topbar, home, each tab) instead of one big custom.css."}
          </p>
        </div>

        {cssMode === "standard" ? (
          <div className="data-dir-row">
            <code className="data-dir-path">{customCssPath ?? "No custom CSS added (optional)"}</code>
            <button className="secondary-button" onClick={() => void browseCustomCss()}><FolderOpen size={14} /> Choose custom .css...</button>
          </div>
        ) : (
          <ul className="theme-maker-tab-list">
            {HYBRID_PAGE_KEYS.map((key) => <li key={key}>
              <div className="data-dir-row" style={{ flex: 1 }}>
                <span style={{ minWidth: 90, fontSize: 12 }}>{HYBRID_PAGE_LABELS[key] ?? key}</span>
                <code className="data-dir-path">{pageCssPaths[key] ?? "Not set (optional)"}</code>
                <button className="secondary-button" onClick={() => void browsePageCss(key)}><FolderOpen size={14} /> Choose...</button>
                {pageCssPaths[key] && <button className="icon-action" title="Clear" onClick={() => clearPageCss(key)}><X size={13} /></button>}
              </div>
            </li>)}
          </ul>
        )}

        <ThemeAiChatPanel
          hasKey={aiHasKey}
          onCssApplied={(path) => setCustomCssPath(path)}
          onPageCssApplied={cssMode === "hybrid" ? (key, path) => { if (path) setPageCssPaths((current) => ({ ...current, [key]: path })); } : undefined}
          draftMode={cssMode === "hybrid"}
        />

        <label>Sidebar position
          <select value={sidebarPosition} onChange={(event) => setSidebarPosition(event.target.value)}>
            {SIDEBAR_POSITIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
        </label>

        <div>
          <strong style={{ fontSize: 12 }}>Tabs (order + visibility)</strong>
          <ul className="theme-maker-tab-list">
            {tabOrder.map((key, index) => <li key={key}>
              <label className="theme-maker-tab-checkbox">
                <input type="checkbox" checked={!hiddenTabs.has(key)} disabled={LOCKED_TABS.has(key)} onChange={() => toggleTab(key)} />
                {TAB_LABELS[key] ?? key}{LOCKED_TABS.has(key) ? " (always visible)" : ""}
              </label>
              <div className="theme-maker-tab-move">
                <button className="icon-action" disabled={index === 0} onClick={() => moveTab(index, -1)}><ArrowUp size={13} /></button>
                <button className="icon-action" disabled={index === tabOrder.length - 1} onClick={() => moveTab(index, 1)}><ArrowDown size={13} /></button>
              </div>
            </li>)}
          </ul>
          <p className="data-dir-note">Home can't be hidden or moved -- it always stays first.</p>
        </div>

        {error && <div className="java-error">{error}</div>}
        {savedPath && <div className="data-dir-note">Saved to: {savedPath}</div>}

        <button className="primary-button" onClick={() => void pack()} disabled={packing}>
          {packing ? <LoaderCircle className="spin" size={14} /> : <Package size={14} />} Export .dftp
        </button>
      </div>
    </section>
  </div>;
}
