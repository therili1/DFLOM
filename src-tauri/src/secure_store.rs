// Encrypts sensitive account data (access/refresh tokens) at rest.
//
// Why: accounts.json previously held Microsoft/Ely.by tokens in plaintext.
// A refresh_token is effectively a long-lived key to the user's Microsoft
// account — any other local process/malware could read it straight off
// disk. We now keep a random 256-bit key in the OS credential store
// (Windows Credential Manager / macOS Keychain / Secret Service on Linux,
// via the `keyring` crate) and use it to AES-256-GCM encrypt the whole
// accounts file. The non-sensitive account list structure itself doesn't
// need to be human-readable on disk, so the entire JSON blob is encrypted
// rather than just individual fields — simpler and leaves no metadata
// (usernames, uuids) readable either.
//
// Migration: if an old plaintext accounts.json is found (starts with `{`
// or `[`), it's read as-is once and immediately rewritten encrypted.

use aes_gcm::aead::{Aead, KeyInit, OsRng};
use aes_gcm::{Aes256Gcm, Nonce};
use base64::{engine::general_purpose::STANDARD, Engine};
use rand::RngCore;

const SERVICE_NAME: &str = "com.dreamfuture.launcher";
const KEY_ENTRY: &str = "accounts-encryption-key";
const NONCE_LEN: usize = 12;

fn keyring_entry() -> Result<keyring::Entry, String> {
    keyring::Entry::new(SERVICE_NAME, KEY_ENTRY).map_err(|error| error.to_string())
}

/// Fetches the AES key from the OS keychain, generating and storing a new
/// random one on first run.
fn get_or_create_key() -> Result<[u8; 32], String> {
    let entry = keyring_entry()?;
    match entry.get_password() {
        Ok(existing) => {
            let bytes = STANDARD
                .decode(existing)
                .map_err(|error| format!("Corrupted encryption key in OS keychain: {error}"))?;
            if bytes.len() != 32 {
                return Err("Encryption key in OS keychain has unexpected length.".into());
            }
            let mut key = [0u8; 32];
            key.copy_from_slice(&bytes);
            Ok(key)
        }
        Err(keyring::Error::NoEntry) => {
            let mut key = [0u8; 32];
            OsRng.fill_bytes(&mut key);
            entry
                .set_password(&STANDARD.encode(key))
                .map_err(|error| format!("Could not save encryption key to OS keychain: {error}"))?;
            Ok(key)
        }
        Err(error) => Err(format!("Could not access OS keychain: {error}")),
    }
}

/// Encrypts `plaintext` and returns a self-contained blob (nonce || ciphertext),
/// base64-encoded so it can be written to a plain text file.
pub fn encrypt(plaintext: &[u8]) -> Result<String, String> {
    let key = get_or_create_key()?;
    let cipher = Aes256Gcm::new_from_slice(&key).map_err(|error| error.to_string())?;
    let mut nonce_bytes = [0u8; NONCE_LEN];
    OsRng.fill_bytes(&mut nonce_bytes);
    let nonce = Nonce::from_slice(&nonce_bytes);
    let ciphertext = cipher
        .encrypt(nonce, plaintext)
        .map_err(|error| format!("Encryption failed: {error}"))?;
    let mut blob = Vec::with_capacity(NONCE_LEN + ciphertext.len());
    blob.extend_from_slice(&nonce_bytes);
    blob.extend_from_slice(&ciphertext);
    Ok(STANDARD.encode(blob))
}

/// Decrypts a blob produced by [`encrypt`].
pub fn decrypt(blob_base64: &str) -> Result<Vec<u8>, String> {
    let key = get_or_create_key()?;
    let blob = STANDARD
        .decode(blob_base64.trim())
        .map_err(|error| format!("Corrupted account data: {error}"))?;
    if blob.len() < NONCE_LEN {
        return Err("Corrupted account data (too short).".into());
    }
    let (nonce_bytes, ciphertext) = blob.split_at(NONCE_LEN);
    let cipher = Aes256Gcm::new_from_slice(&key).map_err(|error| error.to_string())?;
    cipher
        .decrypt(Nonce::from_slice(nonce_bytes), ciphertext)
        .map_err(|_| "Could not decrypt account data (wrong key or corrupted file).".to_string())
}
