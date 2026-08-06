const API = "https://api.modrinth.com/v2";
export async function modrinthFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API}${path}`, { ...init, headers: { Accept: "application/json", ...init?.headers } });
  if (!response.ok) throw new Error(`Modrinth API returned HTTP ${response.status}.`);
  return response.json() as Promise<T>;
}