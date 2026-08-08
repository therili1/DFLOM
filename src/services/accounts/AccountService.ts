import { AccountStorage } from "./AccountStorage";
import { createOfflineAccount, type Account } from "./OfflineAccount";
export const AccountService = {
  list: AccountStorage.list,
  create: async (username: string) => AccountStorage.save(createOfflineAccount(username)),
  save: AccountStorage.save,
  remove: AccountStorage.remove,
  browseSkin: AccountStorage.browseSkin,
};