export type MinecraftVersionType = "release" | "snapshot" | "old_alpha" | "old_beta";

export type MinecraftVersion = {
  id: string;
  type: MinecraftVersionType;
  releaseTime: string;
  url: string;
  sha1: string;
};

type MojangManifest = {
  latest: { release: string; snapshot: string };
  versions: Array<{
    id: string;
    type: "release" | "snapshot" | "old_alpha" | "old_beta";
    releaseTime: string;
    url: string;
    sha1: string;
  }>;
};

const MANIFEST_URL = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

export const MinecraftVersionService = {
  async fetchVersions(): Promise<{ versions: MinecraftVersion[]; latestRelease: string; latestSnapshot: string }> {
    const response = await fetch(MANIFEST_URL, { headers: { Accept: "application/json" } });
    if (!response.ok) {
      throw new Error(`Mojang version manifest returned HTTP ${response.status}.`);
    }
    const manifest = (await response.json()) as MojangManifest;
    return {
      versions: manifest.versions,
      latestRelease: manifest.latest.release,
      latestSnapshot: manifest.latest.snapshot,
    };
  },
};