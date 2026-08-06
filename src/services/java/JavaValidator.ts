export function javaMajor(version: string): number | null {
  const match = version.match(/(?:1\.)?(\d+)/);
  return match ? Number(match[1]) : null;
}
export function requiredJavaMajor(version: string): number {
  // Regular "1.x.y" release/snapshot IDs (everything through the 1.21 line).
  const legacy = version.match(/^1\.(\d+)/);
  if (legacy) {
    const minor = Number(legacy[1]);
    return minor <= 12 ? 8 : minor <= 20 ? 17 : 21;
  }
  // Mojang dropped the "1." prefix in 2026 for a year.drop[.hotfix] scheme
  // ("26.1", "26.2", "26.3", ...) -- there is no "Minecraft 1.22". Starting
  // with 26.1, Minecraft requires Java 25 (Microsoft build of OpenJDK 25),
  // not 21. Since year-based numbering only started in 2026, any id that
  // doesn't match the old "1.x" shape -- a year.drop release id, or a
  // snapshot for one -- is from the 26.1+ line and needs Java 25. (The old
  // code fell through its dotted-index parsing for exactly these ids and
  // silently landed on "needs Java 8" -- the opposite of correct.)
  return 25;
}
export function isCompatible(javaVersion: string, minecraftVersion: string) {
  const major = javaMajor(javaVersion);
  return major === requiredJavaMajor(minecraftVersion);
}