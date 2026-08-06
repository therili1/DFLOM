import { create } from "zustand";
import { AccountService } from "../services/accounts/AccountService";
import { ElyByAccountManager } from "../services/accounts/ElyByAccountManager";
import { MicrosoftAccountManager } from "../services/accounts/MicrosoftAccountManager";
import type { DeviceCodeInfo } from "../services/accounts/MicrosoftAuthService";
import type { Account } from "../services/accounts/OfflineAccount";

type AccountState = {
  accounts: Account[];
  selectedAccount: Account | null;
  loading: boolean;
  error: string | null;
  msDeviceCode: DeviceCodeInfo | null;
  msLoading: boolean;
  loadAccounts: () => Promise<void>;
  refreshElyBy: (account: Account) => Promise<void>;
  createAccount: (username: string) => Promise<void>;
  loginElyBy: (username: string, password: string) => Promise<void>;
  logoutElyBy: (account: Account) => Promise<void>;
  loginMicrosoft: () => Promise<void>;
  cancelMicrosoftLogin: () => void;
  refreshMicrosoft: (account: Account) => Promise<void>;
  logoutMicrosoft: (account: Account) => Promise<void>;
  updateAccount: (account: Account) => Promise<void>;
  deleteAccount: (id: string) => Promise<void>;
  selectAccount: (account: Account) => void;
  toggleFavorite: (account: Account) => Promise<void>;
};

export const useAccountStore = create<AccountState>((set, get) => ({
  accounts: [], selectedAccount: null, loading: false, error: null, msDeviceCode: null, msLoading: false,
  loadAccounts: async () => {
    set({ loading: true, error: null });
    try {
      const stored = await AccountService.list();
      const accounts = await Promise.all(stored.map(async (account) => {
        if (account.type === "Ely.by" && account.accessToken && account.clientToken) {
          try { return await ElyByAccountManager.refresh(account); } catch { return account; }
        }
        if (account.type === "Microsoft" && account.refreshToken) {
          try { return await MicrosoftAccountManager.refresh(account); } catch { return account; }
        }
        return account;
      }));
      set({ accounts, selectedAccount: accounts.find((a) => a.favorite) ?? accounts[0] ?? null, loading: false });
    } catch (e) { set({ loading: false, error: String(e) }); }
  },
  refreshElyBy: async (account) => { set({ loading: true, error: null }); try { const refreshed = await ElyByAccountManager.refresh(account); set((s) => ({ accounts: s.accounts.map((item) => item.id === refreshed.id ? refreshed : item), selectedAccount: s.selectedAccount?.id === refreshed.id ? refreshed : s.selectedAccount, loading: false })); } catch (e) { set({ loading: false, error: String(e) }); throw e; } },
  createAccount: async (username) => { try { const account = await AccountService.create(username); set((s) => ({ accounts: [...s.accounts, account], selectedAccount: account })); } catch (e) { set({ error: String(e) }); throw e; } },
  loginElyBy: async (username, password) => { set({ loading: true, error: null }); try { const account = await ElyByAccountManager.login(username, password); set((s) => ({ accounts: [...s.accounts.filter((item) => item.id !== account.id), account], selectedAccount: account, loading: false })); } catch (e) { set({ loading: false, error: String(e) }); throw e; } },
  logoutElyBy: async (account) => { await ElyByAccountManager.logout(account); set((s) => { const accounts = s.accounts.filter((item) => item.id !== account.id); return { accounts, selectedAccount: s.selectedAccount?.id === account.id ? accounts[0] ?? null : s.selectedAccount }; }); },
  loginMicrosoft: async () => {
    set({ msLoading: true, error: null, msDeviceCode: null });
    try {
      const info = await MicrosoftAccountManager.start();
      set({ msDeviceCode: info });
      const account = await MicrosoftAccountManager.complete(info);
      // Login may have been cancelled while we were waiting on the poll.
      if (!get().msDeviceCode) return;
      set((s) => ({ accounts: [...s.accounts.filter((item) => item.id !== account.id), account], selectedAccount: account, msLoading: false, msDeviceCode: null }));
    } catch (e) { set({ msLoading: false, msDeviceCode: null, error: String(e) }); throw e; }
  },
  cancelMicrosoftLogin: () => set({ msDeviceCode: null, msLoading: false }),
  refreshMicrosoft: async (account) => { set({ loading: true, error: null }); try { const refreshed = await MicrosoftAccountManager.refresh(account); set((s) => ({ accounts: s.accounts.map((item) => item.id === refreshed.id ? refreshed : item), selectedAccount: s.selectedAccount?.id === refreshed.id ? refreshed : s.selectedAccount, loading: false })); } catch (e) { set({ loading: false, error: String(e) }); throw e; } },
  logoutMicrosoft: async (account) => { await MicrosoftAccountManager.logout(account); set((s) => { const accounts = s.accounts.filter((item) => item.id !== account.id); return { accounts, selectedAccount: s.selectedAccount?.id === account.id ? accounts[0] ?? null : s.selectedAccount }; }); },
  updateAccount: async (account) => { const saved = await AccountService.save(account); set((s) => ({ accounts: s.accounts.map((a) => a.id === saved.id ? saved : a), selectedAccount: s.selectedAccount?.id === saved.id ? saved : s.selectedAccount })); },
  deleteAccount: async (id) => { await AccountService.remove(id); set((s) => { const accounts = s.accounts.filter((a) => a.id !== id); return { accounts, selectedAccount: s.selectedAccount?.id === id ? accounts[0] ?? null : s.selectedAccount }; }); },
  selectAccount: (selectedAccount) => set({ selectedAccount }),
  toggleFavorite: async (account) => { await (async () => { const updated = { ...account, favorite: !account.favorite }; await AccountService.save(updated); set((s) => ({ accounts: s.accounts.map((a) => a.id === updated.id ? updated : { ...a, favorite: false }), selectedAccount: updated })); })(); },
}));
