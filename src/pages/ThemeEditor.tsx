import { useEffect, useMemo, useState } from "react";
import { ArrowDown, ArrowUp, Check, LoaderCircle, Plus, Save } from "lucide-react";
import { useThemeEngineStore } from "../stores/themeEngineStore";
import { ThemeEngineService } from "../services/theme/ThemeEngineService";
import ThemeAiChatPanel from "../components/ThemeAiChatPanel";

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
// Hiding either of these would remove the only way to get back in and fix
// the layout, so they can't be unchecked here (Sidebar also enforces this
// as a hard backstop even if a theme file was crafted to bypass this UI).
const LOCKED_TABS = new Set(["settings", "theme-editor"]);
// Same fixed slot set Theme Maker's Hybrid Mode offers -- kept in sync
// manually since this is a small, stable list (see theme.rs sanitize_page_key
// for the actual on-disk validation, which is the real source of truth).
const HYBRID_PAGE_KEYS = ["sidebar", "topbar", "home", ...Object.keys(TAB_LABELS)];
const HYBRID_PAGE_LABELS: Record<string, string> = { sidebar: "Sidebar", topbar: "Topbar", home: "Home", ...TAB_LABELS };

const SIDEBAR_POSITIONS = [
  { value: "left", label: "Left" },
  { value: "right", label: "Right" },
  { value: "top", label: "Top" },
  { value: "bottom", label: "Bottom" },
];

// Theme Editor edits LAYOUT settings (sidebar position, hidden tabs, tab
// order) of an ALREADY INSTALLED .dftp theme -- not its background,
// fonts, or custom CSS, which are creation-time-only (Theme Maker).
export default function ThemeEditor() {
  const { themes, loading, error, loadThemes, updateLayout } = useThemeEngineStore();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [sidebarPosition, setSidebarPosition] = useState("left");
  const [hiddenTabs, setHiddenTabs] = useState<Set<string>>(new Set());
  const [tabOrder, setTabOrder] = useState<string[]>(DEFAULT_TAB_ORDER);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  // Пункт 4: per-page CSS overrides ("Hybrid CSS") for the selected
  // already-installed theme. pageCss holds the current textarea content
  // for every page key that has (or was just given) a pages/*.css file;
  // savingKey/savedKey track per-key save state so multiple boxes don't
  // fight over one shared "saving" flag.
  const [pageCss, setPageCss] = useState<Record<string, string>>({});
  const [loadingPageCss, setLoadingPageCss] = useState(false);
  const [savingKey, setSavingKey] = useState<string | null>(null);
  const [savedKey, setSavedKey] = useState<string | null>(null);
  const [pageCssError, setPageCssError] = useState<string | null>(null);
  const [newPageKey, setNewPageKey] = useState("");
  const [aiHasKey, setAiHasKey] = useState<boolean | null>(null);

  useEffect(() => { void loadThemes(); }, [loadThemes]);
  useEffect(() => { ThemeEngineService.hasGeminiApiKey().then(setAiHasKey).catch(() => setAiHasKey(false)); }, []);

  const selected = useMemo(() => themes.find((t) => t.id === selectedId) ?? null, [themes, selectedId]);

  useEffect(() => {
    if (!selected) return;
    setSidebarPosition(selected.sidebarPosition ?? "left");
    setHiddenTabs(new Set((selected.hiddenTabs ?? []).filter((key) => !LOCKED_TABS.has(key))));
    // Fall back to the default order, but keep any tabs the theme already
    // ordered; unlisted known tabs are appended at the end.
    const known = new Set(DEFAULT_TAB_ORDER);
    const fromTheme = (selected.tabOrder ?? []).filter((key) => known.has(key));
    const remaining = DEFAULT_TAB_ORDER.filter((key) => !fromTheme.includes(key));
    setTabOrder(fromTheme.length ? [...fromTheme, ...remaining] : DEFAULT_TAB_ORDER);
    setSaved(false);
  }, [selected]);

  useEffect(() => {
    setPageCss({});
    setPageCssError(null);
    setSavedKey(null);
    setNewPageKey("");
    if (!selected) return;
    setLoadingPageCss(true);
    ThemeEngineService.readPageCss(selected.id)
      .then(setPageCss)
      .catch((readError) => setPageCssError(readError instanceof Error ? readError.message : String(readError)))
      .finally(() => setLoadingPageCss(false));
  }, [selected]);

  const savePageCss = async (key: string) => {
    if (!selected) return;
    setPageCssError(null);
    setSavingKey(key);
    setSavedKey(null);
    try {
      await ThemeEngineService.writePageCss(selected.id, key, pageCss[key] ?? "");
      setSavedKey(key);
    } catch (writeError) {
      setPageCssError(writeError instanceof Error ? writeError.message : String(writeError));
    } finally {
      setSavingKey(null);
    }
  };

  const addPageCssBlock = () => {
    if (!newPageKey || newPageKey in pageCss) return;
    setPageCss((current) => ({ ...current, [newPageKey]: "" }));
    setNewPageKey("");
  };

  const availableNewKeys = HYBRID_PAGE_KEYS.filter((key) => !(key in pageCss));

  // The AI chat writes straight to pages/<key>.css on disk (theme_write_page_css)
  // when the user applies a page-targeted reply, so just re-read that one
  // key back rather than re-reading everything.
  const onAiPageCssApplied = (pageKey: string) => {
    if (!selected) return;
    ThemeEngineService.readPageCss(selected.id)
      .then((all) => setPageCss((current) => ({ ...current, [pageKey]: all[pageKey] ?? "" })))
      .catch((readError) => setPageCssError(readError instanceof Error ? readError.message : String(readError)));
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

  const save = async () => {
    if (!selectedId) return;
    setSaving(true);
    setSaved(false);
    await updateLayout(selectedId, sidebarPosition, [...hiddenTabs], tabOrder);
    setSaving(false);
    setSaved(true);
  };

  return <div className="settings-page">
    <div className="page-title"><span className="eyebrow">THEME ENGINE</span><h1>Theme Editor</h1><p>Change the layout of an already-installed .dftp theme -- sidebar position, tab order, and which tabs are hidden.</p></div>

    <section className="settings-card">
      <div className="setting-row" style={{ flexDirection: "column", alignItems: "stretch", gap: 14 }}>
        <label>Theme to edit
          <select value={selectedId ?? ""} onChange={(event) => setSelectedId(event.target.value || null)}>
            <option value="">Select an installed theme...</option>
            {themes.map((theme) => <option key={theme.id} value={theme.id}>{theme.name}{theme.isActive ? " (active)" : ""}</option>)}
          </select>
        </label>

        {loading && <div className="data-dir-note"><LoaderCircle className="spin" size={13} /> Loading themes...</div>}
        {!loading && !themes.length && <div className="java-empty"><h3>No theme packs installed</h3><p>Install a .dftp first (Settings -&gt; Theme Packs), then come back here to edit its layout.</p></div>}
        {error && <div className="java-error">{error}</div>}

        {selected && <>
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

          <button className="primary-button" onClick={() => void save()} disabled={saving}>
            {saving ? <LoaderCircle className="spin" size={14} /> : saved ? <Check size={14} /> : <Save size={14} />} {saved ? "Saved" : "Save layout"}
          </button>

          <div>
            <strong style={{ fontSize: 12 }}>Per-page CSS (Hybrid Mode)</strong>
            <p className="data-dir-note">Edit this theme's pages/*.css overrides directly -- separate from the sidebar-injected custom.css.</p>
            {loadingPageCss && <div className="data-dir-note"><LoaderCircle className="spin" size={13} /> Loading page CSS...</div>}
            {pageCssError && <div className="java-error">{pageCssError}</div>}

            {Object.keys(pageCss).length > 0 && <div style={{ display: "flex", flexDirection: "column", gap: 14, marginTop: 8 }}>
              {Object.keys(pageCss).map((key) => <div key={key} className="settings-card" style={{ padding: 12 }}>
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 6 }}>
                  <strong style={{ fontSize: 12 }}>{HYBRID_PAGE_LABELS[key] ?? key} <code style={{ opacity: 0.6 }}>pages/{key}.css</code></strong>
                  <button className="secondary-button" onClick={() => void savePageCss(key)} disabled={savingKey === key}>
                    {savingKey === key ? <LoaderCircle className="spin" size={13} /> : savedKey === key ? <Check size={13} /> : <Save size={13} />}
                    {savedKey === key ? " Saved" : " Save"}
                  </button>
                </div>
                <textarea
                  value={pageCss[key]}
                  onChange={(event) => { setPageCss((current) => ({ ...current, [key]: event.target.value })); setSavedKey(null); }}
                  rows={8}
                  style={{ width: "100%", fontFamily: "monospace", fontSize: 12 }}
                  placeholder={`/* CSS for ${key} */`}
                />
              </div>)}
            </div>}

            {availableNewKeys.length > 0 && <div className="data-dir-row" style={{ marginTop: 10 }}>
              <select value={newPageKey} onChange={(event) => setNewPageKey(event.target.value)}>
                <option value="">Add page CSS block...</option>
                {availableNewKeys.map((key) => <option key={key} value={key}>{HYBRID_PAGE_LABELS[key] ?? key}</option>)}
              </select>
              <button className="secondary-button" onClick={addPageCssBlock} disabled={!newPageKey}><Plus size={14} /> Add</button>
            </div>}

            {selected.isActive && <div style={{ marginTop: 14 }}>
              <ThemeAiChatPanel hasKey={aiHasKey} onCssApplied={() => {}} onPageCssApplied={onAiPageCssApplied} />
            </div>}
            {!selected.isActive && <p className="data-dir-note">Activate this theme to use the AI chat here -- it edits the currently active theme's CSS.</p>}
          </div>
        </>}
      </div>
    </section>
  </div>;
}
