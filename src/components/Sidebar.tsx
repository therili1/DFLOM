import { NavLink } from "react-router-dom";
import { Blocks, Download, Gamepad2, Home, LayoutGrid, Palette, Settings, Store, Terminal, UserRound } from "lucide-react";
import Brand from "./Brand";
import type { NavigationItem } from "../types/navigation";
import { useThemeEngineStore } from "../stores/themeEngineStore";

const items: NavigationItem[] = [
  { label: "Home", path: "/", icon: "home" },
  { label: "Instances", path: "/instances", icon: "instances" },
  { label: "Marketplace", path: "/marketplace", icon: "marketplace" },
  { label: "Downloads", path: "/downloads", icon: "downloads" },
  { label: "Theme Maker", path: "/theme-maker", icon: "theme-maker" },
  { label: "Theme Editor", path: "/theme-editor", icon: "theme-editor" },
  { label: "Logs", path: "/logs", icon: "logs" },
  { label: "Settings", path: "/settings", icon: "settings" },
  { label: "Accounts", path: "/accounts", icon: "accounts" },
];

const icons = { home: Home, instances: Blocks, marketplace: Store, downloads: Download, "theme-maker": Palette, "theme-editor": LayoutGrid, logs: Terminal, settings: Settings, accounts: UserRound };

// Stable key for hiddenTabs/tabOrder matching -- "home" for "/", otherwise
// the path without its leading slash (e.g. "/theme-maker" -> "theme-maker").
function navKey(path: string): string {
  return path === "/" ? "home" : path.slice(1);
}

// "settings" and "theme-editor" can never be hidden -- they're the only
// way to get back into the layout settings if a theme hides everything
// else, so if we ever honored a hidden flag on them the user could lock
// themselves out of their own launcher.
const LOCKED_TABS = new Set(["home", "settings", "theme-editor"]);

export default function Sidebar() {
  const current = useThemeEngineStore((state) => state.current);
  const hidden = new Set(current?.hiddenTabs ?? []);
  const order = current?.tabOrder ?? [];

  const visible = items.filter((item) => LOCKED_TABS.has(navKey(item.path)) || !hidden.has(navKey(item.path)));
  const sorted = order.length
    ? [...visible].sort((a, b) => {
        const ai = order.indexOf(navKey(a.path));
        const bi = order.indexOf(navKey(b.path));
        const aRank = ai === -1 ? order.length + visible.indexOf(a) : ai;
        const bRank = bi === -1 ? order.length + visible.indexOf(b) : bi;
        return aRank - bRank;
      })
    : visible;

  return (
    <aside className="sidebar">
      <Brand />
      <div className="sidebar-divider" />
      <nav className="nav-list" aria-label="Main navigation">
        {sorted.map((item) => {
          const Icon = icons[item.icon as keyof typeof icons];
          return (
            <NavLink key={item.path} to={item.path} end={item.path === "/"} className={({ isActive }) => `nav-item ${isActive ? "active" : ""}`}>
              <Icon size={18} strokeWidth={1.8} />
              <span>{item.label}</span>
            </NavLink>
          );
        })}
      </nav>
      <div className="sidebar-footer">
        <div className="status-dot" />
        <div><strong>Foundation Build</strong><span>Ready for your journey</span></div>
      </div>
    </aside>
  );
}
