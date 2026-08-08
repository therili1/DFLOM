import { invoke } from "@tauri-apps/api/core";

export type Instance = { name: string; minecraftVersion: string; loader: string; loaderVersion?: string; created: string; size: number; gameDirectory?: string; iconPath?: string };
export type CreateInstanceInput = { name: string; minecraftVersion: string; loader: string; loaderVersion?: string; gameDirectory?: string };
export type LaunchInstanceInput = {
  instanceDirectory: string;
  version: string;
  javaPath: string;
  ramMin: number;
  ramMax: number;
  width: number;
  height: number;
  username?: string;
  uuid?: string;
  accessToken?: string;
  userType?: string;
  extraJvmArguments?: string[];
};
export type LaunchResult = { pid: number; command: string };

export const InstanceManager = {
  list: () => invoke<Instance[]>("list_instances"),
  active: () => invoke<Instance | null>("get_active_instance"),
  create: (input: CreateInstanceInput) => invoke<Instance>("create_instance", input),
  delete: (name: string) => invoke<void>("delete_instance", { name }),
  rename: (oldName: string, newName: string) => invoke<Instance>("rename_instance", { oldName, newName }),
  changeFolder: (name: string, folder: string) => invoke<Instance>("change_instance_folder", { name, folder }),
  openFolder: (name: string) => invoke<void>("open_instance_folder", { name }),
  importMrpack: (archivePath: string, instanceName: string) =>
    invoke<Instance>("import_mrpack", { archivePath, instanceName }),
  launch: (input: LaunchInstanceInput) => invoke<LaunchResult>("launch_instance", input),
};