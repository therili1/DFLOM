import { modrinthFetch } from "./ModrinthApi";

export type ModrinthDependency = {
  version_id: string | null;
  project_id: string | null;
  file_name: string | null;
  dependency_type: "required" | "optional" | "incompatible" | "embedded";
};

export type ModrinthVersion = {
  id: string;
  version_number: string;
  date_published: string;
  version_type: string;
  game_versions: string[];
  loaders: string[];
  dependencies: ModrinthDependency[];
  files: Array<{ url: string; filename: string; primary: boolean; size: number }>;
};

export const VersionService = {
  // IMPORTANT: this endpoint returns *every* version ever published for the
  // project if game_versions/loaders aren't passed -- it does NOT inherit
  // the search facets used to find the project. Always forward the current
  // Minecraft version / loader filters here so the version list (and the
  // "preferred" pick derived from it) can't surface builds for a different
  // game version than what's selected in the UI.
  list: (project: string, gameVersions?: string[], loaders?: string[]) => {
    const params = new URLSearchParams();
    if (gameVersions?.length) params.set("game_versions", JSON.stringify(gameVersions));
    if (loaders?.length) params.set("loaders", JSON.stringify(loaders));
    const qs = params.toString();
    return modrinthFetch<ModrinthVersion[]>(`/project/${encodeURIComponent(project)}/version${qs ? `?${qs}` : ""}`);
  },
  get: (id: string) => modrinthFetch<ModrinthVersion>(`/version/${encodeURIComponent(id)}`),
};
