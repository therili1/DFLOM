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
  list: (project: string) => modrinthFetch<ModrinthVersion[]>(`/project/${encodeURIComponent(project)}/version`),
  get: (id: string) => modrinthFetch<ModrinthVersion>(`/version/${encodeURIComponent(id)}`),
};
