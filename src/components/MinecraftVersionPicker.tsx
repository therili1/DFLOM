import { useEffect, useMemo, useRef, useState } from "react";
import { ChevronLeft, ChevronDown, LoaderCircle } from "lucide-react";
import { useVersionStore } from "../stores/versionStore";

type Props = {
  value: string;
  onChange: (versionId: string) => void;
  placeholder?: string;
};

/**
 * A two-step Minecraft version picker: first pick a major.minor group
 * (e.g. "1.21.x"), then pick the exact patch release inside it
 * (e.g. "1.21.11"). Data comes from the official Mojang version manifest.
 */
export default function MinecraftVersionPicker({ value, onChange, placeholder = "All versions" }: Props) {
  const { versions, loading, refreshVersions } = useVersionStore();
  const [open, setOpen] = useState(false);
  const [activeGroup, setActiveGroup] = useState<string | null>(null);
  const [showSnapshots, setShowSnapshots] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => { if (!versions.length) void refreshVersions(); }, [versions.length, refreshVersions]);

  useEffect(() => {
    if (!open) return;
    const onClickOutside = (event: MouseEvent) => { if (rootRef.current && !rootRef.current.contains(event.target as Node)) { setOpen(false); setActiveGroup(null); } };
    document.addEventListener("mousedown", onClickOutside);
    return () => document.removeEventListener("mousedown", onClickOutside);
  }, [open]);

  const groups = useMemo(() => {
    const releases = versions.filter((item) => item.type === "release");
    const map = new Map<string, typeof releases>();
    for (const item of releases) {
      const parts = item.id.split(".");
      const key = parts.length >= 2 ? `${parts[0]}.${parts[1]}` : item.id;
      const bucket = map.get(key);
      if (bucket) bucket.push(item); else map.set(key, [item]);
    }
    return Array.from(map.entries())
      .map(([key, items]) => ({ key, items: items.sort((a, b) => b.releaseTime.localeCompare(a.releaseTime)) }))
      .sort((a, b) => b.items[0].releaseTime.localeCompare(a.items[0].releaseTime));
  }, [versions]);

  const snapshots = useMemo(() =>
    versions.filter((item) => item.type === "snapshot").sort((a, b) => b.releaseTime.localeCompare(a.releaseTime)),
  [versions]);

  const toggle = () => {
    setOpen((state) => !state);
    if (!open) {
      const isSnapshot = snapshots.some((item) => item.id === value);
      setShowSnapshots(isSnapshot);
      setActiveGroup(isSnapshot ? "snapshots" : groups.find((group) => group.items.some((item) => item.id === value))?.key ?? null);
    }
  };

  const pick = (id: string) => { onChange(id); setOpen(false); setActiveGroup(null); };

  return <div className="mc-version-picker" ref={rootRef}>
    <button type="button" className="mc-version-trigger" onClick={toggle}>
      <span>{value || placeholder}</span>
      {loading ? <LoaderCircle className="spin" size={14} /> : <ChevronDown size={14} />}
    </button>
    {open && <div className="mc-version-panel">
      {loading && !versions.length ? <div className="mc-version-loading"><LoaderCircle className="spin" size={13} /> Завантаження версій Mojang…</div> : activeGroup === "snapshots" ? <>
        <button type="button" className="mc-version-back" onClick={() => setActiveGroup(null)}><ChevronLeft size={13} /> Усі групи</button>
        <button type="button" className={`mc-version-option${value === "" ? " selected" : ""}`} onClick={() => pick("")}>Будь-який снапшот</button>
        {snapshots.map((item) => <button
          key={item.id}
          type="button"
          className={`mc-version-option${item.id === value ? " selected" : ""}`}
          onClick={() => pick(item.id)}
        >{item.id}</button>)}
      </> : activeGroup ? <>
        <button type="button" className="mc-version-back" onClick={() => setActiveGroup(null)}><ChevronLeft size={13} /> Усі групи</button>
        <button type="button" className={`mc-version-option${value === "" ? " selected" : ""}`} onClick={() => pick("")}>Будь-яка версія {activeGroup}.x</button>
        {groups.find((group) => group.key === activeGroup)?.items.map((item) => <button
          key={item.id}
          type="button"
          className={`mc-version-option${item.id === value ? " selected" : ""}`}
          onClick={() => pick(item.id)}
        >{item.id}</button>)}
      </> : <>
        <label className="mc-version-toggle">
          <input type="checkbox" checked={showSnapshots} onChange={(event) => setShowSnapshots(event.target.checked)} />
          Показати снапшоти (тестові версії)
        </label>
        <button type="button" className={`mc-version-option${value === "" ? " selected" : ""}`} onClick={() => pick("")}>Усі версії</button>
        {showSnapshots && <button type="button" className="mc-version-group mc-version-group-snapshot" onClick={() => setActiveGroup("snapshots")}>
          <span>Снапшоти</span>
          <span className="mc-version-count">{snapshots.length}</span>
        </button>}
        {groups.map((group) => <button type="button" className="mc-version-group" key={group.key} onClick={() => setActiveGroup(group.key)}>
          <span>{group.key}.x</span>
          <span className="mc-version-count">{group.items.length}</span>
        </button>)}
      </>}
    </div>}
  </div>;
}
