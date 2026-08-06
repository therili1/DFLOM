import MainLayout from "./layouts/MainLayout";

// Page routing itself now lives in MainLayout (see PAGES there) -- every
// page is mounted once and just hidden/shown via CSS instead of being
// unmounted/remounted by <Outlet/> on every navigation, so switching
// sidebar tabs no longer wipes each page's local state. BrowserRouter
// (see main.tsx) still owns the actual URL/history; MainLayout reads
// location.pathname off it directly.
export default function App() {
  return <MainLayout />;
}
