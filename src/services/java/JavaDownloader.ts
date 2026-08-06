import { JavaManager, type JavaInstallation } from "./JavaManager";
export const JavaDownloader = { download: (major: number): Promise<JavaInstallation> => JavaManager.download(major) };