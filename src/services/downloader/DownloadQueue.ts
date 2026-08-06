import { invoke } from "@tauri-apps/api/core";
import type { DownloadTask } from "./DownloadTask";

export const DownloadQueue = {
  async download(task: DownloadTask, versionUrl: string, directory: string): Promise<DownloadTask> {
    const result = await invoke<{ version: string; files: number; bytes: number; directory: string }>("download_version", { versionUrl, version: task.version, instanceDirectory: directory });
    return { ...task, status: "completed", progress: 100, downloadedBytes: result.bytes, totalBytes: result.bytes };
  },
};