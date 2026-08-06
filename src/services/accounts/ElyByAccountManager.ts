import { ElyByAuthService } from "./ElyByAuthService";
import type { Account } from "./OfflineAccount";
export const ElyByAccountManager = {
  login: ElyByAuthService.login,
  refresh: ElyByAuthService.refresh,
  logout: (account: Account) => ElyByAuthService.logout(account),
};