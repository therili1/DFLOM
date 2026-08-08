import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Check, ImageUp, LoaderCircle, Settings2, UserRound, X } from "lucide-react";
import { useAccountStore } from "../stores/accountStore";
import { resolveAvatarSrc } from "../services/accounts/OfflineAccount";
import { AccountService } from "../services/accounts/AccountService";

type Props = { onClose: () => void };

// Opened by clicking the account chip in the topbar (MainLayout). A light
// "quick" surface -- switch which account is selected, and change the
// SELECTED account's skin -- without leaving whatever page you're on.
// Anything heavier (create/delete/sign in) stays on the full Accounts page,
// linked from here via "Manage accounts...".
export default function AccountQuickPanel({ onClose }: Props) {
  const navigate = useNavigate();
  const { accounts, selectedAccount, selectAccount, updateAccount } = useAccountStore();
  const [changingSkin, setChangingSkin] = useState(false);
  const [skinError, setSkinError] = useState<string | null>(null);

  const changeSkin = async () => {
    if (!selectedAccount) return;
    setSkinError(null);
    setChangingSkin(true);
    try {
      const path = await AccountService.browseSkin();
      if (path) await updateAccount({ ...selectedAccount, skinPath: path });
    } catch (error) {
      setSkinError(error instanceof Error ? error.message : String(error));
    } finally {
      setChangingSkin(false);
    }
  };

  const goToAccounts = () => { onClose(); navigate("/accounts"); };

  return <div className="modal-backdrop" onClick={onClose}>
    <div className="modal-card compact-modal" onClick={(event) => event.stopPropagation()}>
      <button type="button" className="modal-close" onClick={onClose}><X size={17} /></button>
      <span className="eyebrow">PROFILE</span>
      <h2>{selectedAccount?.username ?? "No account selected"}</h2>

      {selectedAccount ? <>
        <div style={{ display: "flex", alignItems: "center", gap: 14, marginTop: 4 }}>
          {resolveAvatarSrc(selectedAccount.skinPath)
            ? <img src={resolveAvatarSrc(selectedAccount.skinPath)} alt="" style={{ width: 64, height: 64, borderRadius: 10, objectFit: "cover" }} />
            : <div className="avatar" style={{ width: 64, height: 64 }}><UserRound size={24} /></div>}
          <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
            <button className="secondary-button" onClick={() => void changeSkin()} disabled={changingSkin}>
              {changingSkin ? <LoaderCircle className="spin" size={14} /> : <ImageUp size={14} />} Change skin...
            </button>
            <span className="data-dir-note" style={{ margin: 0 }}>{selectedAccount.type} account &middot; PNG skin file</span>
          </div>
        </div>
        {skinError && <div className="java-error">{skinError}</div>}
      </> : <p className="data-dir-note">Create or sign in to an account first.</p>}

      {accounts.length > 1 && <div style={{ marginTop: 16 }}>
        <strong style={{ fontSize: 12 }}>Switch account</strong>
        <ul className="theme-maker-tab-list" style={{ marginTop: 6 }}>
          {accounts.map((account) => <li key={account.id}>
            <label className="theme-maker-tab-checkbox" style={{ cursor: "pointer" }} onClick={() => selectAccount(account)}>
              {resolveAvatarSrc(account.skinPath)
                ? <img src={resolveAvatarSrc(account.skinPath)} alt="" style={{ width: 22, height: 22, borderRadius: 5, objectFit: "cover", marginRight: 6 }} />
                : <UserRound size={16} style={{ marginRight: 6 }} />}
              {account.username}
              {selectedAccount?.id === account.id && <Check size={13} style={{ marginLeft: "auto" }} />}
            </label>
          </li>)}
        </ul>
      </div>}

      <button className="primary-button modal-submit" onClick={goToAccounts}><Settings2 size={15} /> Manage accounts...</button>
    </div>
  </div>;
}
