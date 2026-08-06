import { ProjectService, type Project } from "./ProjectService";
import { VersionService, type ModrinthVersion } from "./VersionService";

export type ResolvedDependency = { project: Project; version: ModrinthVersion };
export type UnresolvedDependency = { projectId: string; title?: string; reason: string };
export type DependencyResolution = { resolved: ResolvedDependency[]; unresolved: UnresolvedDependency[] };

/**
 * Picks the best available version for a dependency: prefers a version that
 * matches both the target game version and loader, then falls back to
 * matching just the game version (mods with no explicit loader tag, e.g.
 * datapacks), then just the loader, and only uses the newest version as a
 * last resort. Silently grabbing an incompatible fallback was the previous
 * behaviour and could install a dependency build that doesn't actually work
 * with the chosen Minecraft/loader combo, so this is now tracked instead of
 * hidden.
 */
function pickCompatibleVersion(list: ModrinthVersion[], gameVersion: string | undefined, loader: string | undefined): { version: ModrinthVersion | null; exact: boolean } {
  if (!list.length) return { version: null, exact: false };
  const matchesGame = (item: ModrinthVersion) => !gameVersion || item.game_versions.includes(gameVersion);
  const matchesLoader = (item: ModrinthVersion) => !loader || item.loaders.includes(loader);

  const exact = list.find((item) => matchesGame(item) && matchesLoader(item));
  if (exact) return { version: exact, exact: true };

  const gameOnly = list.find((item) => matchesGame(item));
  if (gameOnly) return { version: gameOnly, exact: false };

  const loaderOnly = list.find((item) => matchesLoader(item));
  if (loaderOnly) return { version: loaderOnly, exact: false };

  return { version: list[0], exact: false };
}

/**
 * Walks a version's "required" Modrinth dependencies (recursively, since a
 * dependency can itself depend on something else) and resolves each one to
 * an installable Project + compatible ModrinthVersion. Optional/incompatible
 * dependencies are ignored. Cycles and duplicate projects are only resolved
 * once. The root project is excluded from the result. Anything that can't be
 * resolved to a project, or only to an incompatible version, is reported in
 * `unresolved` instead of being dropped without a trace.
 */
async function resolveRecursive(
  version: ModrinthVersion,
  gameVersion: string | undefined,
  loader: string | undefined,
  rootProjectId: string,
  visited: Set<string>,
  resolved: Map<string, ResolvedDependency>,
  unresolved: Map<string, UnresolvedDependency>,
): Promise<void> {
  const required = version.dependencies.filter((dep) => dep.dependency_type === "required" && dep.project_id);
  for (const dep of required) {
    const projectId = dep.project_id as string;
    if (projectId === rootProjectId || visited.has(projectId)) continue;
    visited.add(projectId);

    const project = await ProjectService.get(projectId).catch(() => null);

    let depVersion: ModrinthVersion | null = null;
    if (dep.version_id) {
      depVersion = await VersionService.get(dep.version_id).catch(() => null);
    } else {
      try {
        const list = await VersionService.list(projectId);
        const { version: picked, exact } = pickCompatibleVersion(list, gameVersion, loader);
        if (picked && !exact) {
          unresolved.set(projectId, { projectId, title: project?.title, reason: "No build matches the selected Minecraft version/loader — skipped to avoid installing an incompatible file." });
          continue;
        }
        depVersion = picked;
      } catch {
        // fall through to the unresolved report below
      }
    }

    if (!depVersion) {
      unresolved.set(projectId, { projectId, title: project?.title, reason: "Could not find a downloadable version for this dependency." });
      continue;
    }
    if (!project) {
      unresolved.set(projectId, { projectId, reason: "Could not load project details for this dependency." });
      continue;
    }

    resolved.set(projectId, { project, version: depVersion });
    await resolveRecursive(depVersion, gameVersion, loader, rootProjectId, visited, resolved, unresolved);
  }
}

export const DependencyResolver = {
  resolveRequired: async (version: ModrinthVersion, rootProjectId: string, gameVersion?: string, loader?: string): Promise<DependencyResolution> => {
    const resolved = new Map<string, ResolvedDependency>();
    const unresolved = new Map<string, UnresolvedDependency>();
    await resolveRecursive(version, gameVersion, loader, rootProjectId, new Set([rootProjectId]), resolved, unresolved);
    return { resolved: Array.from(resolved.values()), unresolved: Array.from(unresolved.values()) };
  },
};
