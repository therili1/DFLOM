use crate::{read_accounts, write_accounts, Account};
use serde::{Deserialize, Serialize};
use tauri::AppHandle;

// Client ID registered in Azure (Personal Microsoft accounts only, public client).
const CLIENT_ID: &str = "2ac39376-bc3c-46ca-b1f4-d0cd83a5c7b6";
const SCOPE: &str = "XboxLive.signin offline_access";

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DeviceCodeInfo {
    pub device_code: String,
    pub user_code: String,
    pub verification_uri: String,
    pub expires_in: u64,
    pub interval: u64,
}

#[derive(Debug, Deserialize)]
struct DeviceCodeResponse {
    device_code: String,
    user_code: String,
    verification_uri: String,
    expires_in: u64,
    interval: u64,
}

#[derive(Debug, Deserialize)]
struct MsTokenResponse {
    access_token: String,
    refresh_token: Option<String>,
}

#[derive(Debug, Deserialize)]
struct MsTokenErrorResponse {
    error: String,
}

#[derive(Debug, Deserialize)]
struct XblClaims {
    xui: Vec<XblUhs>,
}
#[derive(Debug, Deserialize)]
struct XblUhs {
    uhs: String,
}
#[derive(Debug, Deserialize)]
struct XblResponse {
    #[serde(rename = "Token")]
    token: String,
    #[serde(rename = "DisplayClaims")]
    display_claims: XblClaims,
}

#[derive(Debug, Deserialize)]
struct McLoginResponse {
    access_token: String,
}

#[derive(Debug, Deserialize)]
struct McProfileSkin {
    url: String,
}
#[derive(Debug, Deserialize)]
struct McProfileCape {
    url: String,
}
#[derive(Debug, Deserialize)]
struct McProfileResponse {
    id: String,
    name: String,
    #[serde(default)]
    skins: Vec<McProfileSkin>,
    #[serde(default)]
    capes: Vec<McProfileCape>,
}

fn client() -> reqwest::blocking::Client {
    reqwest::blocking::Client::new()
}

/// Step 1: request a device code the user enters at microsoft.com/link
#[tauri::command]
pub async fn ms_login_start() -> Result<DeviceCodeInfo, String> {
    tauri::async_runtime::spawn_blocking(move || ms_login_start_impl())
        .await
        .map_err(|error| error.to_string())?
}

pub fn ms_login_start_impl() -> Result<DeviceCodeInfo, String> {
    let response = client()
        .post("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode")
        .form(&[("client_id", CLIENT_ID), ("scope", SCOPE)])
        .send()
        .map_err(|e| e.to_string())?;
    if !response.status().is_success() {
        let status = response.status();
        let body = response.text().unwrap_or_else(|_| "Не вдалося почати вхід через Microsoft.".into());
        return Err(format!("Не вдалося почати вхід через Microsoft (код {status}): {body}"));
    }
    let body: DeviceCodeResponse = response.json().map_err(|e| e.to_string())?;
    Ok(DeviceCodeInfo {
        device_code: body.device_code,
        user_code: body.user_code,
        verification_uri: body.verification_uri,
        expires_in: body.expires_in,
        interval: body.interval,
    })
}


/// Step 2: poll until the user finishes signing in in their browser, then
/// walk the Xbox Live -> XSTS -> Minecraft token chain and save the account.
#[tauri::command]
pub async fn ms_login_complete(app: AppHandle, device_code: String, interval: u64, expires_in: u64) -> Result<Account, String> {
    tauri::async_runtime::spawn_blocking(move || ms_login_complete_impl(app, device_code, interval, expires_in))
        .await
        .map_err(|error| error.to_string())?
}

pub fn ms_login_complete_impl(app: AppHandle, device_code: String, interval: u64, expires_in: u64) -> Result<Account, String> {
    let ms_token = poll_for_ms_token(&device_code, interval, expires_in)?;
    finish_login(app, ms_token.access_token, ms_token.refresh_token)
}


fn poll_for_ms_token(device_code: &str, interval: u64, expires_in: u64) -> Result<MsTokenResponse, String> {
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(expires_in);
    let mut wait_secs = interval.max(2);
    loop {
        if std::time::Instant::now() >= deadline {
            return Err("Час на вхід через Microsoft вичерпано. Спробуй ще раз.".into());
        }
        std::thread::sleep(std::time::Duration::from_secs(wait_secs));
        let response = client()
            .post("https://login.microsoftonline.com/consumers/oauth2/v2.0/token")
            .form(&[
                ("client_id", CLIENT_ID),
                ("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                ("device_code", device_code),
            ])
            .send()
            .map_err(|e| e.to_string())?;
        if response.status().is_success() {
            return response.json::<MsTokenResponse>().map_err(|e| e.to_string());
        }
        let err: MsTokenErrorResponse = response.json().unwrap_or(MsTokenErrorResponse { error: "unknown_error".into() });
        match err.error.as_str() {
            "authorization_pending" => continue,
            "slow_down" => { wait_secs += 5; continue; }
            "expired_token" => return Err("Час на вхід через Microsoft вичерпано. Спробуй ще раз.".into()),
            "authorization_declined" => return Err("Вхід через Microsoft скасовано.".into()),
            other => return Err(format!("Помилка Microsoft: {other}")),
        }
    }
}

fn xbox_live_auth(ms_access_token: &str) -> Result<XblResponse, String> {
    let response = client()
        .post("https://user.auth.xboxlive.com/user/authenticate")
        .json(&serde_json::json!({
            "Properties": { "AuthMethod": "RPS", "SiteName": "user.auth.xboxlive.com", "RpsTicket": format!("d={ms_access_token}") },
            "RelyingParty": "http://auth.xboxlive.com",
            "TokenType": "JWT"
        }))
        .send()
        .map_err(|e| e.to_string())?;
    if !response.status().is_success() {
        let status = response.status();
        let body = response.text().unwrap_or_default();
        return Err(format!("Не вдалося авторизуватись через Xbox Live (код {status}): {body}"));
    }
    response.json().map_err(|e| e.to_string())
}

fn xsts_auth(xbl_token: &str) -> Result<XblResponse, String> {
    let response = client()
        .post("https://xsts.auth.xboxlive.com/xsts/authorize")
        .json(&serde_json::json!({
            "Properties": { "SandboxId": "RETAIL", "UserTokens": [xbl_token] },
            "RelyingParty": "rp://api.minecraftservices.com/",
            "TokenType": "JWT"
        }))
        .send()
        .map_err(|e| e.to_string())?;
    if response.status().as_u16() == 401 {
        let body = response.text().unwrap_or_default();
        return Err(format!("Цей Microsoft-акаунт не має Xbox-профілю, або регіон не підтримується: {body}"));
    }
    if !response.status().is_success() {
        let status = response.status();
        let body = response.text().unwrap_or_default();
        return Err(format!("Не вдалося отримати XSTS-токен (код {status}): {body}"));
    }
    response.json().map_err(|e| e.to_string())
}

fn finish_login(app: AppHandle, ms_access_token: String, ms_refresh_token: Option<String>) -> Result<Account, String> {
    let xbl = xbox_live_auth(&ms_access_token)?;
    let uhs = xbl.display_claims.xui.first().ok_or("Не вдалося отримати Xbox user hash.")?.uhs.clone();
    let xsts = xsts_auth(&xbl.token)?;

    let mc_response = client()
        .post("https://api.minecraftservices.com/authentication/login_with_xbox")
        .json(&serde_json::json!({ "identityToken": format!("XBL3.0 x={uhs};{}", xsts.token) }))
        .send()
        .map_err(|e| e.to_string())?;
    if !mc_response.status().is_success() {
        let status = mc_response.status();
        let body = mc_response.text().unwrap_or_default();
        return Err(format!("Не вдалося увійти в Minecraft-сервіси (код {status}): {body}"));
    }
    let mc_login: McLoginResponse = mc_response.json().map_err(|e| e.to_string())?;

    // Confirm the account actually owns Minecraft (a bare Microsoft/Xbox
    // account without a purchase would fail login or the profile fetch below).
    let profile_response = client()
        .get("https://api.minecraftservices.com/minecraft/profile")
        .bearer_auth(&mc_login.access_token)
        .send()
        .map_err(|e| e.to_string())?;
    if profile_response.status().as_u16() == 404 {
        return Err("На цьому Microsoft-акаунті не куплено Minecraft.".into());
    }
    if !profile_response.status().is_success() {
        let status = profile_response.status();
        let body = profile_response.text().unwrap_or_default();
        return Err(format!("Не вдалося отримати профіль Minecraft (код {status}): {body}"));
    }
    let profile: McProfileResponse = profile_response.json().map_err(|e| e.to_string())?;

    let account = Account {
        id: profile.id.clone(),
        username: profile.name,
        uuid: profile.id,
        r#type: "Microsoft".into(),
        created_at: chrono::Utc::now().to_rfc3339(),
        last_played: None,
        skin_path: profile.skins.first().map(|s| s.url.clone()).unwrap_or_default(),
        cape_path: profile.capes.first().map(|c| c.url.clone()).unwrap_or_default(),
        favorite: false,
        email: None,
        access_token: Some(mc_login.access_token),
        client_token: None,
        refresh_token: ms_refresh_token,
    };

    let mut accounts = read_accounts(&app)?;
    accounts.retain(|item| item.id != account.id);
    accounts.push(account.clone());
    write_accounts(&app, &accounts)?;
    Ok(account)
}

/// Use the stored Microsoft refresh token to get a fresh Minecraft access
/// token without asking the user to sign in again.
#[tauri::command]
pub async fn ms_refresh(app: AppHandle, account: Account) -> Result<Account, String> {
    tauri::async_runtime::spawn_blocking(move || ms_refresh_impl(app, account))
        .await
        .map_err(|error| error.to_string())?
}

pub fn ms_refresh_impl(app: AppHandle, account: Account) -> Result<Account, String> {
    let refresh_token = account.refresh_token.clone().ok_or("Немає refresh-токена, потрібен повторний вхід.")?;
    let response = client()
        .post("https://login.microsoftonline.com/consumers/oauth2/v2.0/token")
        .form(&[
            ("client_id", CLIENT_ID),
            ("grant_type", "refresh_token"),
            ("refresh_token", refresh_token.as_str()),
            ("scope", SCOPE),
        ])
        .send()
        .map_err(|e| e.to_string())?;
    if !response.status().is_success() {
        return Err("Сесію Microsoft прострочено, увійди повторно.".into());
    }
    let token: MsTokenResponse = response.json().map_err(|e| e.to_string())?;
    finish_login(app, token.access_token, token.refresh_token.or(Some(refresh_token)))
}


#[tauri::command]
pub async fn ms_logout(app: AppHandle, account: Account) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || ms_logout_impl(app, account))
        .await
        .map_err(|error| error.to_string())?
}

pub fn ms_logout_impl(app: AppHandle, account: Account) -> Result<(), String> {
    let mut accounts = read_accounts(&app)?;
    accounts.retain(|item| item.id != account.id);
    write_accounts(&app, &accounts)
}

