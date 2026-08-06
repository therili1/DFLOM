import { useEffect, type ReactNode } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { Bell, Search, UserRound, WifiOff } from "lucide-react";
import { isTauri, convertFileSrc } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import Sidebar from "../components/Sidebar";
import Home from "../pages/Home";
import Instances from "../pages/Instances";
import Marketplace from "../pages/Marketplace";
import Downloads from "../pages/Downloads";
import Settings from "../pages/Settings";
import Accounts from "../pages/Accounts";
import Logs from "../pages/Logs";
import ThemeMaker from "../pages/ThemeMaker";
import ThemeEditor from "../pages/ThemeEditor";
import { useThemeStore } from "../stores/themeStore";
import { useThemeEngineStore } from "../stores/themeEngineStore";
import { useAccountStore } from "../stores/accountStore";
import { useInstanceStore } from "../stores/InstanceStore";
import { useLogsStore } from "../stores/logsStore";
import { useAccentStore } from "../stores/accentStore";

// Every page is mounted ONCE, up front, and just hidden (display: none)
// while its tab isn't active -- switching tabs used to unmount/remount
// the page component via <Outlet/>, which wiped all of its local state
// (search filters, in-progress forms, scroll position, everything) on
// every switch. Keeping them all alive fixes that; the trade-off is that
// every page's own mount-time effects (initial data fetches etc.) fire
// once, up front, for all pages -- not lazily on first visit.
const PAGES: { path: string; key: string; element: ReactNode }[] = [
  { path: "/", key: "home", element: <Home /> },
  { path: "/instances", key: "instances", element: <Instances /> },
  { path: "/marketplace", key: "marketplace", element: <Marketplace /> },
  { path: "/downloads", key: "downloads", element: <Downloads /> },
  { path: "/theme-maker", key: "theme-maker", element: <ThemeMaker /> },
  { path: "/theme-editor", key: "theme-editor", element: <ThemeEditor /> },
  { path: "/settings", key: "settings", element: <Settings /> },
  { path: "/accounts", key: "accounts", element: <Accounts /> },
  { path: "/logs", key: "logs", element: <Logs /> },
];

export default function MainLayout() {
  const location = useLocation();
  const navigate = useNavigate();
  const isDark = useThemeStore((state) => state.isDark);
  const selectedAccount = useAccountStore((state) => state.selectedAccount);
  const loadInstances = useInstanceStore((state) => state.loadInstances);
  const loadAccounts = useAccountStore((state) => state.loadAccounts);
  const applyAccent = useAccentStore((state) => state.apply);
  const activeTheme = useThemeEngineStore((state) => state.current);
  const loadCurrentTheme = useThemeEngineStore((state) => state.loadCurrent);
  const applyPageCss = useThemeEngineStore((state) => state.applyPageCss);
  const pageCssMap = useThemeEngineStore((state) => state.pageCssMap);
  const title = location.pathname === "/" ? "Home" : location.pathname.slice(1).replace("-", " ");
  useEffect(() => { void loadInstances(); }, [loadInstances]);
  useEffect(() => { void loadAccounts(); }, [loadAccounts]);
  useEffect(() => { applyAccent(); }, [applyAccent]);
  useEffect(() => { void loadCurrentTheme(); }, [loadCurrentTheme]);
  // "Hybrid CSS" mode: re-apply pages/<page>.css (plus the always-on
  // sidebar.css/topbar.css) every time the route changes. pageCssMap is a
  // dependency too so switching themes (which reloads the map) re-applies
  // immediately instead of waiting for the next navigation.
  useEffect(() => {
    const pageKey = location.pathname === "/" ? "home" : location.pathname.slice(1);
    applyPageCss(pageKey);
  }, [location.pathname, applyPageCss, pageCssMap]);
  useEffect(() => {
    if (!PAGES.some((page) => page.path === location.pathname)) navigate("/", { replace: true });
  }, [location.pathname, navigate]);
  useEffect(() => {
    if (!isTauri()) return;
    const unlistenLog = listen<{ pid: number; stream: "stdout" | "stderr"; line: string }>("minecraft-log", (event) => {
      useLogsStore.getState().appendLine(event.payload.pid, event.payload.stream, event.payload.line);
    });
    const unlistenExit = listen<{ pid: number; code: number | null }>("minecraft-exit", (event) => {
      useLogsStore.getState().endSession(event.payload.pid, event.payload.code);
    });
    return () => { void unlistenLog.then((dispose) => dispose()); void unlistenExit.then((dispose) => dispose()); };
  }, []);
  // "left" is the default layout (no modifier class needed -- matches the
  // original, unmodified CSS). Only top/bottom/right get an explicit class.
  const sidebarPosition = activeTheme?.sidebarPosition ?? "left";
  const positionClass = sidebarPosition !== "left" ? ` app-shell--${sidebarPosition}` : "";
  const backgroundPath = activeTheme?.backgroundPath ?? null;
  const backgroundIsVideo = backgroundPath ? ["mp4", "webm"].includes(backgroundPath.split(".").pop()?.toLowerCase() ?? "") : false;
  const backgroundSrc = backgroundPath ? convertFileSrc(backgroundPath) : null;
  return (
    <div className={`app-shell ${isDark ? "theme-dark" : "theme-light"}${positionClass}${backgroundSrc ? " app-shell--themed-bg" : ""}`}>
      {backgroundSrc && (backgroundIsVideo
        ? <video className="app-shell-background" src={backgroundSrc} autoPlay loop muted playsInline />
        : <img className="app-shell-background" src={backgroundSrc} alt="" />)}
      <Sidebar />
      <main className="main-area">
        <header className="topbar">
            <div className="breadcrumb"><span>Dream Future Launcher</span><b>/</b><strong>{title}</strong></div>
          <div className="topbar-actions">
            <button className="offline-button"><WifiOff size={14} /> Offline</button>
            <button className="icon-button" aria-label="Search"><Search size={17} /></button>
            <button className="icon-button" aria-label="Notifications"><Bell size={17} /><i /></button>
            <div className="profile">{selectedAccount?.skinPath ? <img className="profile-avatar-image" src={selectedAccount.skinPath} alt="" /> : <div className="avatar"><UserRound size={15} /></div>}<span>{selectedAccount?.username ?? "Guest profile"}</span></div>
          </div>
        </header>
        <div className="page-content">
          {PAGES.map((page) => (
            <div key={page.key} style={{ display: location.pathname === page.path ? "contents" : "none" }}>
              {page.element}
            </div>
          ))}
        </div>
      </main>
    </div>
  );
}
