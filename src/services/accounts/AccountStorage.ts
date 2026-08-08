import { invoke } from "@tauri-apps/api/core";
import type { Account } from "./OfflineAccount";
export const AccountStorage = {
  list: () => invoke<Account[]>("list_accounts"),
  save: (account: Account) => invoke<Account>("save_account", { account }),
  remove: (id: string) => invoke<void>("remove_account", { id }),
  browseSkin: () => invoke<string | null>("browse_skin_file"),
};