import { MicrosoftAuthService } from "./MicrosoftAuthService";
import type { Account } from "./OfflineAccount";
export const MicrosoftAccountManager = {
  start: MicrosoftAuthService.start,
  complete: MicrosoftAuthService.complete,
  refresh: MicrosoftAuthService.refresh,
  logout: (account: Account) => MicrosoftAuthService.logout(account),
};
