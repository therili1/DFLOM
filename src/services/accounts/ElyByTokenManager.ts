import type { Account } from "./OfflineAccount";
export const ElyByTokenManager = {
  hasToken: (account: Account) => Boolean(account.type === "Ely.by" && account.accessToken && account.clientToken),
};