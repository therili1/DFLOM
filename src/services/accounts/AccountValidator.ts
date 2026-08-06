export function validateUsername(username: string): string | null {
  if (!/^[A-Za-z0-9_]{3,16}$/.test(username)) return "Username must be 3–16 characters and contain only letters, numbers, or underscores.";
  return null;
}