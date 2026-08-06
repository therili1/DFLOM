import { invoke } from "@tauri-apps/api/core";
import type { Account } from "./OfflineAccount";
export const ElyByAuthService = {
  login: (username: string, password: string) => invoke<Account>("ely_login", { username, password }),
  refresh: (account: Account) => invoke<Account>("ely_refresh", { account }),
  logout: (account: Account) => invoke<void>("ely_logout", { account }),
};