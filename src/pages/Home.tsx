import { useEffect, useState } from "react";
import { Camera, FileArchive, Globe2, LoaderCircle } from "lucide-react";
import { convertFileSrc } from "@tauri-apps/api/core";
import { InstanceContentService, type WorldEntry, type ScreenshotEntry } from "../services/instance/InstanceContentService";

const TABS = [
  { value: "worlds", label: "Світи" },
  { value: "screenshots", label: "Скріншоти" },
] as const;

export default function Home() {
  const [tab, setTab] = useState<(typeof TABS)[number]["value"]>("worlds");
  const [worlds, setWorlds] = useState<WorldEntry[]>([]);
  const [screenshots, setScreenshots] = useState<ScreenshotEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [installingFor, setInstallingFor] = useState<string | null>(null);

  const loadWorlds = async () => {
    setLoading(true);
    setError(null);
    try {
      setWorlds(await InstanceContentService.listAllWorlds());
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : String(loadError));
    } finally {
      setLoading(false);
    }
  };

  const loadScreenshots = async () => {
    setLoading(true);
    setError(null);
    try {
      setScreenshots(await InstanceContentService.listAllScreenshots());
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : String(loadError));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (tab === "worlds") void loadWorlds();
    else void loadScreenshots();
  }, [tab]);

  const installDatapack = async (world: WorldEntry) => {
    const key = `${world.instanceName}/${world.worldName}`;
    setError(null);
    try {
      const path = await InstanceContentService.browseDatapack();
      if (!path) return;
      setInstallingFor(key);
      await InstanceContentService.installWorldDatapack(world.instanceName, world.worldName, path);
    } catch (installError) {
      setError(installError instanceof Error ? installError.message : String(installError));
    } finally {
      setInstallingFor(null);
    }
  };

  return (
    <div className="home-page">
      <div className="page-title"><span className="eyebrow">DREAM FUTURE LAUNCHER</span><h1>Home</h1><p>Your worlds and screenshots, across every instance.</p></div>

      <div className="market-tabs">
        {TABS.map((item) => <button key={item.value} className={tab === item.value ? "active" : ""} onClick={() => setTab(item.value)}>{item.label}</button>)}
      </div>

      {error && <div className="java-error">{error}</div>}
      {loading && <div className="data-dir-note"><LoaderCircle className="spin" size={13} /> Loading...</div>}

      {tab === "worlds" && !loading && (
        worlds.length
          ? <div className="theme-list">
              {worlds.map((world) => {
                const key = `${world.instanceName}/${world.worldName}`;
                return <article className="theme-card" key={key}>
                  <div className="theme-pack-preview theme-pack-preview--placeholder"><Globe2 size={20} /></div>
                  <div className="theme-card-info"><h3>{world.worldName}</h3><code>{world.instanceName}</code></div>
                  <div className="java-actions">
                    <button onClick={() => void installDatapack(world)} disabled={installingFor === key}>
                      {installingFor === key ? <LoaderCircle className="spin" size={13} /> : <FileArchive size={13} />} Встановити датапак
                    </button>
                  </div>
                </article>;
              })}
            </div>
          : <div className="java-empty"><Globe2 size={23} /><h3>Світів ще немає</h3><p>Створіть світ у грі, і він з'явиться тут.</p></div>
      )}

      {tab === "screenshots" && !loading && (
        screenshots.length
          ? <div className="screenshot-grid">
              {screenshots.map((shot) => <figure className="screenshot-item" key={`${shot.instanceName}/${shot.filename}`}>
                <img src={convertFileSrc(shot.path)} alt={shot.filename} loading="lazy" />
                <figcaption>{shot.instanceName}</figcaption>
              </figure>)}
            </div>
          : <div className="java-empty"><Camera size={23} /><h3>Скріншотів ще немає</h3><p>Зробіть скріншот у грі (F2), і він з'явиться тут.</p></div>
      )}
    </div>
  );
}
