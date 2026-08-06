import { invoke } from "@tauri-apps/api/core";
import type { Account } from "./OfflineAccount";

export type DeviceCodeInfo = { deviceCode: string; userCode: string; verificationUri: string; expiresIn: number; interval: number };

export const MicrosoftAuthService = {
  start: () => invoke<DeviceCodeInfo>("ms_login_start"),
  complete: (info: DeviceCodeInfo) => invoke<Account>("ms_login_complete", { deviceCode: info.deviceCode, interval: info.interval, expiresIn: info.expiresIn }),
  refresh: (account: Account) => invoke<Account>("ms_refresh", { account }),
  logout: (account: Account) => invoke<void>("ms_logout", { account }),
};
