import { useEffect, useMemo, useState } from "react";
import { ArrowDown, ArrowUp, Check, LoaderCircle, Save } from "lucide-react";
import { useThemeEngineStore } from "../stores/themeEngineStore";

const TAB_LABELS: Record<string, string> = {
  instances: "Instances",
  marketplace: "Marketplace",
  downloads: "Downloads",
  "theme-maker": "Theme Maker",
  "theme-editor": "Theme Editor",
  logs: "Logs",
  settings: "Settings",
  accounts: "Accounts",
};
const DEFAULT_TAB_ORDER = Object.keys(TAB_LABELS);
// Hiding either of these would remove the only way to get back in and fix
// the layout, so they can't be unchecked here (Sidebar also enforces this
// as a hard backstop even if a theme file was crafted to bypass this UI).
const LOCKED_TABS = new Set(["settings", "theme-editor"]);

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

  useEffect(() => { void loadThemes(); }, [loadThemes]);

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
        </>}
      </div>
    </section>
  </div>;
}
