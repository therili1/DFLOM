import { modrinthFetch } from "./ModrinthApi";
import type { Project } from "./ProjectService";
export type SearchResult = { hits: Project[]; offset: number; limit: number; total_hits: number };
export type SearchOptions = { query: string; projectType: string; version: string; loader: string; sort: string; offset: number };
export async function searchProjects(options: SearchOptions) {
  const facets = [[`project_type:${options.projectType}`], options.version ? [`versions:${options.version}`] : [], options.loader ? [`categories:${options.loader}`] : []].filter((facet) => facet.length);
  const params = new URLSearchParams({ query: options.query, limit: "20", offset: String(options.offset), index: options.sort });
  if (facets.length) params.set("facets", JSON.stringify(facets));
  return modrinthFetch<SearchResult>(`/search?${params}`);
}