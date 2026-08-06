import { invoke } from "@tauri-apps/api/core";

export async function greet(name: string): Promise<string> {
  return invoke<string>("greet", { name });
}