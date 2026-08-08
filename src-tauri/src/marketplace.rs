// Supabase-backed Theme Marketplace — list/search/sort themes hosted on a
// Supabase project, download+install one through the existing Theme Engine
// (theme::theme_install_impl, unmodified), and rate/upload themes through
// Supabase Edge Functions.
//
// The Publishable ("anon") key used here is a fixed, internal launcher
// configuration -- it is NOT a secret (Supabase publishable/anon keys are
// meant to be shipped in client apps, same as e.g. a Firebase web config;
// real access control lives in Postgres RLS policies + Edge Function
// logic on the backend). It is never a service_role key, never entered by
// the user, and never exposed in Settings.
use serde::{Deserialize, Serialize};
use std::{fs, time::Duration};
use tauri::AppHandle;

use crate::{data_root, http_client};
use crate::theme::{theme_install_impl, ThemeInfo};

const SUPABASE_URL: &str = "https://nsupmqvlanakljvtfjak.supabase.co";
const SUPABASE_PUBLISHABLE_KEY: &str = "sb_publishable_GZ_9byLqNKKZSBRjLek2qw_lgSxgslS";

pub(crate) const SHARED_SUPABASE_URL: &str = SUPABASE_URL;
pub(crate) const SHARED_SUPABASE_PUBLISHABLE_KEY: &str = SUPABASE_PUBLISHABLE_KEY;

fn functions_url(path: &str) -> String {
    format!("{SUPABASE_URL}/functions/v1/{path}")
}

fn rest_url(path: &str) -> String {
    format!("{SUPABASE_URL}/rest/v1/{path}")
}

fn authed_request(_app: &AppHandle, method: reqwest::Method, url: &str) -> Result<reqwest::blocking::RequestBuilder, String> {
    Ok(http_client()
        .request(method, url)
        .header("apikey", SUPABASE_PUBLISHABLE_KEY)
        .header("Authorization", format!("Bearer {SUPABASE_PUBLISHABLE_KEY}"))
        .timeout(Duration::from_secs(20)))
}

/// Lightweight connectivity check used by the UI to show
/// "Marketplace Connected" / an error, without the user ever touching a
/// key. Hits the `themes` table with a `limit=1` REST read -- cheap, and
/// exercises the real Publishable-key + RLS path end to end.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MarketplaceStatus {
    pub connected: bool,
    pub message: String,
}

#[tauri::command]
pub async fn marketplace_status(app: AppHandle) -> Result<MarketplaceStatus, String> {
    tauri::async_runtime::spawn_blocking(move || Ok(marketplace_status_impl(&app)))
        .await.map_err(|error| error.to_string())?
}

fn marketplace_status_impl(app: &AppHandle) -> MarketplaceStatus {
    let url = rest_url("themes?select=id&limit=1");
    let request = match authed_request(app, reqwest::Method::GET, &url) {
        Ok(request) => request,
        Err(error) => return MarketplaceStatus { connected: false, message: error },
    };
    match request.send() {
        Ok(response) if response.status().is_success() => {
            MarketplaceStatus { connected: true, message: "Marketplace Connected".to_string() }
        }
        Ok(response) => {
            let status = response.status();
            let body = response.text().unwrap_or_default();
            MarketplaceStatus { connected: false, message: marketplace_error_message(status.as_u16(), &body) }
        }
        Err(error) => MarketplaceStatus { connected: false, message: format!("Could not reach the theme marketplace: {error}") },
    }
}

// ── list_themes ────────────────────────────────────────────────────────

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MarketplaceTheme {
    pub id: String,
    pub name: String,
    #[serde(default)]
    pub author: String,
    #[serde(default)]
    pub description: String,
    #[serde(default, alias = "preview_url", alias = "previewUrl")]
    pub preview: Option<String>,
    #[serde(default, alias = "download_count", alias = "downloads_count")]
    pub downloads: u64,
    #[serde(default)]
    pub rating: f32,
    #[serde(default, alias = "rating_count", alias = "ratings_count")]
    pub rating_count: u64,
    #[serde(default, alias = "created_at")]
    pub created_at: Option<String>,
}

#[derive(Deserialize)]
struct ListThemesResponse {
    #[serde(default, alias = "themes")]
    data: Vec<MarketplaceTheme>,
}

/// The `list_themes` Edge Function returns `{ "data": [...], "error": null }`.
/// Some Edge Functions instead wrap under `themes`, or just return a bare
/// JSON array -- accept all three shapes rather than guessing wrong and
/// breaking on a backend detail we don't control.
fn parse_list_themes(body: &str) -> Result<Vec<MarketplaceTheme>, String> {
    if let Ok(wrapped) = serde_json::from_str::<ListThemesResponse>(body) {
        if !wrapped.data.is_empty() { return Ok(wrapped.data); }
    }
    serde_json::from_str::<Vec<MarketplaceTheme>>(body)
        .or_else(|_| serde_json::from_str::<ListThemesResponse>(body).map(|w| w.data))
        .map_err(|error| format!("Unexpected response from the theme marketplace: {error}"))
}

#[tauri::command]
pub async fn marketplace_list_themes(app: AppHandle, query: String, sort: String, page: u32) -> Result<Vec<MarketplaceTheme>, String> {
    tauri::async_runtime::spawn_blocking(move || marketplace_list_themes_impl(app, query, sort, page))
        .await.map_err(|error| error.to_string())?
}

fn marketplace_list_themes_impl(app: AppHandle, query: String, sort: String, page: u32) -> Result<Vec<MarketplaceTheme>, String> {
    let sort_param = match sort.as_str() {
        "new" | "newest" => "new",
        "rating" | "top" => "rating",
        _ => "popular",
    };
    let mut url = functions_url("list_themes");
    url.push_str(&format!("?sort={sort_param}&page={page}"));
    if !query.trim().is_empty() {
        url.push_str(&format!("&search={}", urlencoding_encode(query.trim())));
    }

    let response = authed_request(&app, reqwest::Method::GET, &url)?
        .send()
        .map_err(|error| format!("Could not reach the theme marketplace: {error}"))?;
    let status = response.status();
    let body = response.text().map_err(|error| error.to_string())?;
    if !status.is_success() {
        return Err(marketplace_error_message(status.as_u16(), &body));
    }
    parse_list_themes(&body)
}

fn marketplace_error_message(status: u16, body: &str) -> String {
    match status {
        401 | 403 => "The theme marketplace rejected the saved API key. Check the Publishable key in Settings.".to_string(),
        404 => "The theme marketplace endpoint could not be found (it may have moved).".to_string(),
        429 => "The theme marketplace is rate-limiting requests right now — try again shortly.".to_string(),
        500..=599 => "The theme marketplace is temporarily unavailable. Try again later.".to_string(),
        _ => {
            let snippet: String = body.chars().take(200).collect();
            if snippet.trim().is_empty() { format!("The theme marketplace returned an error (HTTP {status}).") }
            else { format!("The theme marketplace returned an error (HTTP {status}): {snippet}") }
        }
    }
}

// Minimal percent-encoding for a search query string -- avoids pulling in
// a whole crate just for this one field.
fn urlencoding_encode(input: &str) -> String {
    let mut out = String::with_capacity(input.len());
    for byte in input.bytes() {
        match byte {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => out.push(byte as char),
            b' ' => out.push('+'),
            _ => out.push_str(&format!("%{byte:02X}")),
        }
    }
    out
}

// ── download_theme + install ──────────────────────────────────────────

#[derive(Deserialize)]
struct DownloadThemeResponse {
    #[serde(alias = "downloadUrl", alias = "url")]
    download_url: String,
}

/// Downloads a marketplace theme's .dftp and installs it through the
/// existing, untouched Theme Engine (theme_install_impl) — so manifest
/// validation, custom.css/pages sanitization, and background handling all
/// stay exactly as they are for locally-added themes.
#[tauri::command]
pub async fn marketplace_download_theme(app: AppHandle, theme_id: String) -> Result<ThemeInfo, String> {
    tauri::async_runtime::spawn_blocking(move || marketplace_download_theme_impl(app, theme_id))
        .await.map_err(|error| error.to_string())?
}

fn marketplace_download_theme_impl(app: AppHandle, theme_id: String) -> Result<ThemeInfo, String> {
    // Ask the backend for a download link (this is also where a download
    // gets counted server-side, per the API contract).
    let resolve_response = http_client()
        .post(functions_url("download_theme"))
        .header("apikey", SUPABASE_PUBLISHABLE_KEY)
        .header("Authorization", format!("Bearer {SUPABASE_PUBLISHABLE_KEY}"))
        .json(&serde_json::json!({ "theme_id": theme_id }))
        .timeout(Duration::from_secs(20))
        .send()
        .map_err(|error| format!("Could not reach the theme marketplace: {error}"))?;
    let status = resolve_response.status();
    let body = resolve_response.text().map_err(|error| error.to_string())?;
    if !status.is_success() {
        return Err(marketplace_error_message(status.as_u16(), &body));
    }
    let resolved: DownloadThemeResponse = serde_json::from_str(&body)
        .map_err(|_| "The marketplace didn't return a valid download link for this theme.".to_string())?;

    // Fetch the actual .dftp bytes.
    let file_response = http_client().get(&resolved.download_url).send()
        .map_err(|error| format!("Could not download the theme file: {error}"))?;
    if !file_response.status().is_success() {
        return Err(format!("The theme file could not be downloaded (HTTP {}). It may have been removed.", file_response.status().as_u16()));
    }
    let bytes = file_response.bytes().map_err(|error| error.to_string())?;
    if bytes.is_empty() {
        return Err("The theme file was empty or removed from storage.".to_string());
    }

    let staging_dir = data_root(&app)?.join("launcher-data").join("marketplace-cache").join("downloads");
    fs::create_dir_all(&staging_dir).map_err(|error| error.to_string())?;
    let staged_path = staging_dir.join(format!("{theme_id}.dftp"));
    fs::write(&staged_path, &bytes).map_err(|error| error.to_string())?;

    // theme_install_impl already validates the manifest and rejects
    // anything malformed (bad manifest.json, missing declared background,
    // not a real zip, etc) -- surface its error as-is, it's already
    // written to be user-facing.
    let result = theme_install_impl(app.clone(), staged_path.to_string_lossy().into_owned());
    let _ = fs::remove_file(&staged_path);
    result
}

// ── rate_theme ─────────────────────────────────────────────────────────

#[tauri::command]
pub async fn marketplace_rate_theme(app: AppHandle, theme_id: String, rating: u8) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || marketplace_rate_theme_impl(app, theme_id, rating))
        .await.map_err(|error| error.to_string())?
}

fn marketplace_rate_theme_impl(app: AppHandle, theme_id: String, rating: u8) -> Result<(), String> {
    if !(1..=5).contains(&rating) { return Err("Rating must be between 1 and 5.".to_string()); }
    let response = authed_request(&app, reqwest::Method::POST, &functions_url("rate_theme"))?
        .json(&serde_json::json!({ "theme_id": theme_id, "rating": rating }))
        .send()
        .map_err(|error| format!("Could not reach the theme marketplace: {error}"))?;
    let status = response.status();
    if !status.is_success() {
        let body = response.text().unwrap_or_default();
        return Err(marketplace_error_message(status.as_u16(), &body));
    }
    Ok(())
}

// ── upload_theme ───────────────────────────────────────────────────────
// Backend wiring only for now -- there's no "Publish to Marketplace" button
// in the UI yet (Theme Maker still only exports a local .dftp). Kept here
// so that piece can be added later without touching this module.

#[tauri::command]
pub async fn marketplace_upload_theme(app: AppHandle, archive_path: String, name: String, description: String) -> Result<String, String> {
    tauri::async_runtime::spawn_blocking(move || marketplace_upload_theme_impl(app, archive_path, name, description))
        .await.map_err(|error| error.to_string())?
}

fn marketplace_upload_theme_impl(app: AppHandle, archive_path: String, name: String, description: String) -> Result<String, String> {
    let path = std::path::PathBuf::from(&archive_path);
    if !path.is_file() { return Err("The .dftp file could not be found.".to_string()); }
    let bytes = fs::read(&path).map_err(|error| error.to_string())?;
    use base64::Engine;
    let encoded = base64::engine::general_purpose::STANDARD.encode(&bytes);

    let response = authed_request(&app, reqwest::Method::POST, &functions_url("upload_theme"))?
        .json(&serde_json::json!({ "name": name, "description": description, "file_base64": encoded }))
        .send()
        .map_err(|error| format!("Could not reach the theme marketplace: {error}"))?;
    let status = response.status();
    let body = response.text().map_err(|error| error.to_string())?;
    if !status.is_success() {
        return Err(marketplace_error_message(status.as_u16(), &body));
    }
    Ok(body)
}


