import { useEffect, useState } from "react";
import { Download, LoaderCircle, Search, Star, Store } from "lucide-react";
import { ThemeMarketplaceService, type MarketplaceSort, type MarketplaceTheme } from "../services/marketplace/ThemeMarketplaceService";
import { ThemeMarketplaceCache } from "../services/marketplace/ThemeMarketplaceCache";
import { useThemeEngineStore } from "../stores/themeEngineStore";

const SORT_OPTIONS: { value: MarketplaceSort; label: string }[] = [
  { value: "popular", label: "Популярні" },
  { value: "new", label: "Нові" },
  { value: "rating", label: "Рейтинг" },
];

function formatDownloads(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}K`;
  return String(n);
}

export default function ThemeMarketplace() {
  const [connectionState, setConnectionState] = useState<"checking" | "connected" | "error">("checking");
  const [connectionMessage, setConnectionMessage] = useState<string>("");
  const [query, setQuery] = useState("");
  const [sort, setSort] = useState<MarketplaceSort>("popular");
  const [page, setPage] = useState(0);
  const [themes, setThemes] = useState<MarketplaceTheme[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [installingId, setInstallingId] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [ratingId, setRatingId] = useState<string | null>(null);

  const { loadThemes: refreshInstalledThemes } = useThemeEngineStore();

  const cacheKey = () => JSON.stringify({ query, sort, page });

  const load = async (forceRefresh = false) => {
    setError(null);
    const key = cacheKey();
    if (!forceRefresh) {
      const cached = ThemeMarketplaceCache.get(key);
      if (cached) { setThemes(cached); return; }
    }
    setLoading(true);
    try {
      const results = await ThemeMarketplaceService.listThemes(query, sort, page);
      setThemes(results);
      ThemeMarketplaceCache.set(key, results);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : String(loadError));
    } finally {
      setLoading(false);
    }
  };

  // The Marketplace connects automatically using the launcher's built-in
  // Supabase config -- there is nothing for the user to configure. This
  // just checks connectivity once (so a network hiccup shows a clean
  // message instead of every list_themes call failing silently), then
  // loads the (possibly cached) list. Re-fetches (respecting the cache's
  // own TTL) every time this page is opened, and immediately whenever
  // sort/page/query change.
  useEffect(() => {
    ThemeMarketplaceService.checkConnection().then((result) => {
      setConnectionState(result.connected ? "connected" : "error");
      setConnectionMessage(result.message);
      if (result.connected) void load();
    }).catch((checkError) => {
      setConnectionState("error");
      setConnectionMessage(checkError instanceof Error ? checkError.message : String(checkError));
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (connectionState === "connected") void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sort, page]);

  const runSearch = () => { setPage(0); void load(true); };

  const install = async (theme: MarketplaceTheme) => {
    setError(null); setStatus(null);
    setInstallingId(theme.id);
    try {
      await ThemeMarketplaceService.install(theme.id);
      setStatus(`"${theme.name}" встановлено. Активуй її в Theme Editor.`);
      await refreshInstalledThemes();
    } catch (installError) {
      setError(installError instanceof Error ? installError.message : String(installError));
    } finally {
      setInstallingId(null);
    }
  };

  const rate = async (theme: MarketplaceTheme, value: number) => {
    setError(null);
    setRatingId(theme.id);
    try {
      await ThemeMarketplaceService.rate(theme.id, value);
      setStatus(`Дякуємо за оцінку "${theme.name}"!`);
      void load(true);
    } catch (rateError) {
      setError(rateError instanceof Error ? rateError.message : String(rateError));
    } finally {
      setRatingId(null);
    }
  };

  if (connectionState === "checking") {
    return <div className="market-empty">
      <LoaderCircle className="spin" size={24} />
      <h2>Підключення до Marketplace…</h2>
    </div>;
  }

  if (connectionState === "error") {
    return <div className="market-empty">
      <Store size={24} />
      <h2>Marketplace недоступний</h2>
      <p>{connectionMessage || "Не вдалося підключитися до Theme Marketplace. Спробуй пізніше."}</p>
    </div>;
  }

  return <div>
    <div className="market-status" style={{ marginBottom: 12 }}>Marketplace Connected</div>
    <section className="market-controls">
      <div className="market-search">
        <Search size={16} />
        <input
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          onKeyDown={(event) => { if (event.key === "Enter") runSearch(); }}
          placeholder="Пошук тем..."
        />
      </div>
      <select value={sort} onChange={(event) => { setSort(event.target.value as MarketplaceSort); setPage(0); }}>
        {SORT_OPTIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
      </select>
      <button className="primary-button" onClick={runSearch} disabled={loading}>
        {loading ? <LoaderCircle className="spin" size={15} /> : <Search size={15} />} Пошук
      </button>
    </section>

    {error && <div className="java-error">{error}</div>}
    {status && <div className="market-status">{status}</div>}

    {loading && !themes ? (
      <div className="market-grid">{Array.from({ length: 8 }, (_, i) => <div className="market-skeleton" key={i} />)}</div>
    ) : themes && themes.length ? (
      <div className="market-grid">
        {themes.map((theme) => <article className="project-card" key={theme.id}>
          {theme.preview
            ? <img loading="lazy" src={theme.preview} alt="" />
            : <div className="theme-pack-preview theme-pack-preview--placeholder" />}
          <div className="project-content">
            <h2>{theme.name}</h2>
            <p>{theme.description}</p>
            <span className="project-author">by {theme.author || "невідомий автор"}</span>
            <div className="project-meta">
              <span><Download size={13} /> {formatDownloads(theme.downloads)}</span>
              <span><Star size={13} /> {theme.rating.toFixed(1)} ({theme.ratingCount})</span>
            </div>
            <div className="project-actions" style={{ flexWrap: "wrap", gap: 8 }}>
              <button className="primary-button" disabled={installingId === theme.id} onClick={() => void install(theme)}>
                {installingId === theme.id ? <LoaderCircle className="spin" size={14} /> : <Download size={14} />} Install
              </button>
              <div style={{ display: "flex", gap: 2 }}>
                {[1, 2, 3, 4, 5].map((value) => (
                  <button
                    key={value}
                    className="icon-action"
                    title={`Оцінити ${value}/5`}
                    disabled={ratingId === theme.id}
                    onClick={() => void rate(theme, value)}
                  >
                    <Star size={13} fill={value <= Math.round(theme.rating) ? "currentColor" : "none"} />
                  </button>
                ))}
              </div>
            </div>
          </div>
        </article>)}
      </div>
    ) : (
      <div className="market-empty"><Store size={24} /><h2>Тем не знайдено</h2><p>Спробуй інший запит або сортування.</p></div>
    )}

    <div className="pagination">
      <button disabled={page === 0 || loading} onClick={() => setPage((p) => p - 1)}>Previous</button>
      <span>Сторінка {page + 1}</span>
      <button disabled={loading || !themes || themes.length === 0} onClick={() => setPage((p) => p + 1)}>Next</button>
    </div>
  </div>;
}
