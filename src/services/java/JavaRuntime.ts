import { javaMajor, requiredJavaMajor } from "./JavaValidator";
import type { JavaInstallation } from "./JavaManager";
export const JavaRuntime = {
  requiredFor: (minecraftVersion: string) => requiredJavaMajor(minecraftVersion),
  compatible: (java: JavaInstallation, minecraftVersion: string) => javaMajor(java.version) === requiredJavaMajor(minecraftVersion),
};