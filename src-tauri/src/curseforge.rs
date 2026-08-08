// CurseForge integration -- routed entirely through a Supabase Edge
// Function (`dfl-curseforge-api`), same pattern as marketplace.rs. The
// launcher NEVER sees or stores the real CurseForge API key: that key
// lives only in Supabase Vault, read server-side by the Edge Function via
// `Deno.env.get("curseforge_api_key")`. This file only ever talks to our
// own Supabase project, using the public "Publishable" (anon) key, which
// is safe to ship in a client the same way marketplace.rs's key is.
//
// Edge Function routes this hits (see the architecture doc this was built
// from):
//   GET /dfl-curseforge-api/mods/search?query=...    -> search_mods
//   GET /dfl-curseforge-api/mods/{id}                -> mod_info
//   GET /dfl-curseforge-api/mods/{id}/files           -> mod_files
//
// Every call first checks the `curseforge_cache` table (30 min TTL) before
// hitting the Edge Function, to keep us well under CurseForge's rate
// limits and make repeated marketplace browsing snappy.

use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::{env, fs, io::Read, io::Write, time::Duration};
use tauri::AppHandle;

use crate::http_client;
use crate::marketplace::{SHARED_SUPABASE_PUBLISHABLE_KEY, SHARED_SUPABASE_URL};
use crate::{emit_progress, instance_path, sanitize_relative_path, Instance};

const CACHE_TTL_MINUTES: i64 = 30;

// Bumped when the request shape for a given endpoint+query changes, so
// stale cache rows from an older launcher version don't get served back.
const CACHE_VERSION: &str = "v1";

fn functions_url(path: &str) -> String {
    format!("{SHARED_SUPABASE_URL}/functions/v1/dfl-curseforge-api{path}")
}

fn rest_url(path: &str) -> String {
    format!("{SHARED_SUPABASE_URL}/rest/v1/{path}")
}

fn authed(method: reqwest::Method, url: &str) -> reqwest::blocking::RequestBuilder {
    http_client()
        .request(method, url)
        .header("apikey", SHARED_SUPABASE_PUBLISHABLE_KEY)
        .header("Authorization", format!("Bearer {SHARED_SUPABASE_PUBLISHABLE_KEY}"))
        .timeout(Duration::from_secs(20))
}

/// Cache key is (endpoint, query) -- e.g. endpoint="mods/search",
/// query="query=sodium", or endpoint="mods/12345/files", query="".
fn cache_key(endpoint: &str, query: &str) -> String {
    format!("{CACHE_VERSION}:{endpoint}?{query}")
}

/// Looks up a fresh (<30 min old) cache row. Cache misses, expired rows,
/// and any network/parse error are all treated the same way: `None`, so
/// the caller just falls through to a live CurseForge fetch. A cache
/// problem should never be the reason a mod search fails for the user.
fn read_cache(endpoint: &str, query: &str) -> Option<Value> {
    let key = cache_key(endpoint, query);
    let url = rest_url(&format!(
        "curseforge_cache?endpoint=eq.{}&query=eq.{}&select=response_json,created_at&order=created_at.desc&limit=1",
        urlencoding_light(endpoint),
        urlencoding_light(&key),
    ));
    let response = authed(reqwest::Method::GET, &url).send().ok()?;
    if !response.status().is_success() { return None; }
    let rows: Vec<Value> = response.json().ok()?;
    let row = rows.into_iter().next()?;
    let created_at = row.get("created_at")?.as_str()?;
    let created_at = chrono::DateTime::parse_from_rfc3339(created_at).ok()?;
    let age = chrono::Utc::now().signed_duration_since(created_at.with_timezone(&chrono::Utc));
    if age.num_minutes() >= CACHE_TTL_MINUTES { return None; }
    row.get("response_json").cloned()
}

/// Best-effort cache write. Uses `Prefer: resolution=merge-duplicates`
/// with the (endpoint, query) unique index so repeated writes for the
/// same lookup update the row instead of piling up duplicates. If this
/// fails for any reason we just log it via the error string being
/// dropped -- caching is a performance optimization, never a requirement
/// for the mod data itself reaching the user.
fn write_cache(endpoint: &str, query: &str, response_json: &Value) {
    let key = cache_key(endpoint, query);
    let url = rest_url("curseforge_cache?on_conflict=endpoint,query");
    let body = serde_json::json!({
        "endpoint": endpoint,
        "query": key,
        "response_json": response_json,
    });
    let _ = authed(reqwest::Method::POST, &url)
        .header("Prefer", "resolution=merge-duplicates")
        .json(&body)
        .send();
}

/// Small dependency-free encoder for the handful of characters that show
/// up in mod search queries / cache keys and would otherwise break the
/// PostgREST `eq.` filter syntax.
pub(crate) fn urlencoding_light(input: &str) -> String {
    let mut out = String::with_capacity(input.len());
    for byte in input.bytes() {
        match byte {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => out.push(byte as char),
            _ => out.push_str(&format!("%{:02X}", byte)),
        }
    }
    out
}

fn fetch_through_edge_function(endpoint: &str, query: &str) -> Result<Value, String> {
    if let Some(cached) = read_cache(endpoint, query) {
        return Ok(cached);
    }
    let mut url = functions_url(&format!("/{endpoint}"));
    if !query.is_empty() {
        url.push('?');
        url.push_str(query);
    }
    let response = authed(reqwest::Method::GET, &url)
        .send()
        .map_err(|error| format!("Could not reach CurseForge service: {error}"))?;
    if !response.status().is_success() {
        let status = response.status();
        let text = response.text().unwrap_or_default();
        return Err(format!("CurseForge service returned {status}: {text}"));
    }
    let json: Value = response
        .json()
        .map_err(|error| format!("Unexpected response from CurseForge service: {error}"))?;
    write_cache(endpoint, query, &json);
    Ok(json)
}

// Reserved for future Bedrock support -- CurseForge's gameId differs per
// edition. Only Java (432) is wired up today; adding Bedrock later is
// just adding a match arm here, nothing else in this file changes.
#[allow(dead_code)]
fn game_id_for_edition(edition: &str) -> u32 {
    match edition {
        "bedrock" => 0, // not yet known / not yet supported
        _ => 432,       // Minecraft: Java Edition
    }
}

#[tauri::command]
pub async fn search_curseforge_mods(app: AppHandle, query: String, class_id: Option<u32>) -> Result<Value, String> {
    let _ = app;
    tauri::async_runtime::spawn_blocking(move || {
        // classId narrows results to a CurseForge project class -- 6 =
        // Mods, 4471 = Modpacks (Minecraft, gameId 432). The Edge Function
        // forwards unknown query params to the real CurseForge API
        // untouched, so this is a no-op if it's ever omitted.
        let query_string = match class_id {
            Some(id) => format!("query={}&classId={id}", urlencoding_light(&query)),
            None => format!("query={}", urlencoding_light(&query)),
        };
        fetch_through_edge_function("mods/search", &query_string)
    })
    .await
    .map_err(|error| error.to_string())?
}

#[tauri::command]
pub async fn get_curseforge_mod(mod_id: String) -> Result<Value, String> {
    tauri::async_runtime::spawn_blocking(move || fetch_through_edge_function(&format!("mods/{mod_id}"), ""))
        .await
        .map_err(|error| error.to_string())?
}

#[tauri::command]
pub async fn get_curseforge_mod_files(mod_id: String) -> Result<Value, String> {
    tauri::async_runtime::spawn_blocking(move || {
        fetch_through_edge_function(&format!("mods/{mod_id}/files"), "")
    })
    .await
    .map_err(|error| error.to_string())?
}

// ── CurseForge modpack import ───────────────────────────────────────────
//
// A CurseForge modpack zip (unlike Modrinth's .mrpack) bundles NO mod
// jars inside itself either -- it's manifest.json (which mod+file IDs
// are needed) plus an `overrides/` folder (configs, resource packs,
// anything the pack author wants copied verbatim). The pipeline is:
//
//   download the pack zip -> extract manifest.json + overrides/
//   -> for each {projectID, fileID} in manifest.json, resolve its real
//      download URL via the CurseForge API (the manifest only ever
//      stores ids, never URLs) -> download every mod jar into mods/
//   -> copy overrides/ up into the instance root (configs, resourcepacks, etc.)
//   -> write instance.json so the instance shows up ready to launch.
//
// Every step reuses the same `download-progress` event lib.rs already
// emits for Modrinth installs, so the existing progress bar in
// Marketplace.tsx works for this without any changes on that side.

#[derive(Debug, Deserialize)]
struct CurseManifest {
    minecraft: CurseMinecraft,
    #[serde(default)]
    overrides: String,
    #[serde(default)]
    files: Vec<CurseManifestFile>,
}

#[derive(Debug, Deserialize)]
struct CurseMinecraft {
    version: String,
    #[serde(rename = "modLoaders", default)]
    mod_loaders: Vec<CurseModLoader>,
}

#[derive(Debug, Deserialize)]
struct CurseModLoader {
    id: String,
    #[serde(default)]
    primary: bool,
}

#[derive(Debug, Deserialize)]
struct CurseManifestFile {
    #[serde(rename = "projectID")]
    project_id: u64,
    #[serde(rename = "fileID")]
    file_id: u64,
    #[serde(default = "default_required")]
    required: bool,
}
fn default_required() -> bool { true }

/// Maps a CurseForge `modLoaders[].id` (e.g. "forge-47.2.0",
/// "fabric-0.15.7") to the exact capitalized loader name the rest of the
/// launcher (Instances.tsx's launch flow) compares against, plus the
/// loader's own version string.
fn loader_from_curseforge_id(id: &str) -> (&'static str, Option<String>) {
    let mut parts = id.splitn(2, '-');
    let prefix = parts.next().unwrap_or("").to_ascii_lowercase();
    let version = parts.next().map(|value| value.to_string());
    match prefix.as_str() {
        "forge" => ("Forge", version),
        "neoforge" => ("NeoForge", version),
        "fabric" => ("Fabric", version),
        "quilt" => ("Quilt", version),
        _ => ("Vanilla", None),
    }
}

/// Resolves one manifest file entry to its actual download URL + size by
/// asking the CurseForge API for that specific file (the manifest itself
/// only ever stores the numeric ids). Some mods disable third-party
/// distribution and come back with a null downloadUrl -- callers treat
/// that as a soft failure (skip + report), not a fatal one, so the rest
/// of the pack still installs.
fn resolve_curseforge_file(project_id: u64, file_id: u64) -> Result<(String, String, u64), String> {
    let endpoint = format!("mods/{project_id}/files/{file_id}");
    let json = fetch_through_edge_function(&endpoint, "")?;
    let data = json.get("data").unwrap_or(&json);
    let download_url = data.get("downloadUrl").and_then(|v| v.as_str())
        .ok_or_else(|| "This file's author disabled third-party downloads on CurseForge; install it manually.".to_string())?;
    let file_name = data.get("fileName").and_then(|v| v.as_str()).unwrap_or("file.jar").to_string();
    let file_length = data.get("fileLength").and_then(|v| v.as_u64()).unwrap_or(0);
    Ok((download_url.to_string(), file_name, file_length))
}

fn extract_curseforge_zip(source: &std::path::Path, path: &std::path::PathBuf) -> Result<(), String> {
    let file = fs::File::open(source).map_err(|error| error.to_string())?;
    let mut archive = zip::ZipArchive::new(file).map_err(|_| "The modpack archive is corrupted.".to_string())?;
    for index in 0..archive.len() {
        let mut entry = archive.by_index(index).map_err(|error| error.to_string())?;
        let Some(entry_path) = entry.enclosed_name().map(|path| path.to_owned()) else {
            let _ = fs::remove_dir_all(path);
            return Err("The modpack archive contains an unsafe path.".into());
        };
        let destination = path.join(sanitize_relative_path(&entry_path));
        if entry.is_dir() {
            fs::create_dir_all(&destination).map_err(|error| error.to_string())?;
        } else {
            if let Some(parent) = destination.parent() { fs::create_dir_all(parent).map_err(|error| format!("Could not create folder {}: {error}", parent.display()))?; }
            let mut output = fs::File::create(&destination).map_err(|error| format!("Could not create file {}: {error}", destination.display()))?;
            std::io::copy(&mut entry, &mut output).map_err(|error| error.to_string())?;
        }
    }
    Ok(())
}

/// Copies everything under `<path>/<overrides>` up into `path` itself
/// (configs, resourcepacks, extra mods some pack authors bundle directly
/// instead of listing in manifest.json, etc.), then removes the now-empty
/// overrides folder. Existing files at the destination are overwritten --
/// overrides are meant to win, same as every other CurseForge-compatible
/// launcher's behavior.
fn merge_overrides(path: &std::path::Path, overrides_dir_name: &str) -> Result<(), String> {
    if overrides_dir_name.trim().is_empty() { return Ok(()); }
    let overrides_path = path.join(overrides_dir_name);
    if !overrides_path.is_dir() { return Ok(()); }
    copy_dir_recursive(&overrides_path, path)?;
    let _ = fs::remove_dir_all(&overrides_path);
    Ok(())
}

fn copy_dir_recursive(source: &std::path::Path, destination: &std::path::Path) -> Result<(), String> {
    for entry in fs::read_dir(source).map_err(|error| error.to_string())? {
        let entry = entry.map_err(|error| error.to_string())?;
        let target = destination.join(entry.file_name());
        let file_type = entry.file_type().map_err(|error| error.to_string())?;
        if file_type.is_dir() {
            fs::create_dir_all(&target).map_err(|error| error.to_string())?;
            copy_dir_recursive(&entry.path(), &target)?;
        } else {
            if let Some(parent) = target.parent() { fs::create_dir_all(parent).map_err(|error| error.to_string())?; }
            fs::copy(entry.path(), &target).map_err(|error| error.to_string())?;
        }
    }
    Ok(())
}

fn download_zip_to_temp(app: &AppHandle, task_id: &str, url: &str, display_name: &str) -> Result<std::path::PathBuf, String> {
    let temp_file = env::temp_dir().join(format!("dfl-cf-{}.zip", uuid::Uuid::new_v4()));
    let mut response = http_client().get(url).send().map_err(|error| format!("Could not download modpack: {error}"))?;
    if !response.status().is_success() {
        return Err(format!("CurseForge modpack download returned HTTP {}", response.status()));
    }
    let total = response.content_length().unwrap_or(0);
    let mut file = fs::File::create(&temp_file).map_err(|error| error.to_string())?;
    let mut buffer = [0u8; 65536];
    let mut written: u64 = 0;
    loop {
        let read = response.read(&mut buffer).map_err(|error| format!("error decoding response body: {error}"))?;
        if read == 0 { break; }
        file.write_all(&buffer[..read]).map_err(|error| error.to_string())?;
        written += read as u64;
        emit_progress(app, task_id, display_name, 0, 1, written, total, false);
    }
    emit_progress(app, task_id, display_name, 1, 1, written, total.max(written), true);
    Ok(temp_file)
}

fn download_mod_file(app: &AppHandle, task_id: &str, url: &str, destination: &std::path::Path, display_name: &str, file_index: usize, file_total: usize, size_hint: u64) -> Result<(), String> {
    if let Some(parent) = destination.parent() { fs::create_dir_all(parent).map_err(|error| format!("Could not create folder {}: {error}", parent.display()))?; }
    let mut response = http_client().get(url).send().map_err(|error| error.to_string())?;
    if !response.status().is_success() {
        return Err(format!("Download returned HTTP {} for {display_name}", response.status()));
    }
    let mut file = fs::File::create(destination).map_err(|error| format!("Could not create file {}: {error}", destination.display()))?;
    let mut buffer = [0u8; 65536];
    let mut written: u64 = 0;
    loop {
        let read = response.read(&mut buffer).map_err(|error| format!("error decoding response body: {error}"))?;
        if read == 0 { break; }
        file.write_all(&buffer[..read]).map_err(|error| error.to_string())?;
        written += read as u64;
        emit_progress(app, task_id, display_name, file_index, file_total, written, size_hint.max(written), false);
    }
    Ok(())
}

/// What install_curseforge_modpack resolves to: the created (fully
/// launchable) instance, plus any mods that couldn't be fetched -- e.g.
/// because their author disabled third-party CurseForge downloads. A
/// non-empty `warnings` list is NOT an error: the instance is real and
/// ready to run, just possibly missing a handful of mods the user may
/// need to grab manually from CurseForge's own site.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CurseforgeModpackInstallResult {
    instance: Instance,
    warnings: Vec<String>,
}

#[tauri::command]
pub async fn install_curseforge_modpack(app: AppHandle, download_url: String, file_name: String, instance_name: String, icon_url: Option<String>) -> Result<CurseforgeModpackInstallResult, String> {
    tauri::async_runtime::spawn_blocking(move || install_curseforge_modpack_impl(app, download_url, file_name, instance_name, icon_url))
        .await
        .map_err(|error| error.to_string())?
}

fn install_curseforge_modpack_impl(app: AppHandle, download_url: String, file_name: String, instance_name: String, icon_url: Option<String>) -> Result<CurseforgeModpackInstallResult, String> {
    let path = instance_path(&app, &instance_name)?;
    if path.exists() { return Err("An instance with this name already exists.".into()); }
    let task_id = uuid::Uuid::new_v4().to_string();

    let result = (|| -> Result<CurseforgeModpackInstallResult, String> {
        // 1. Download the modpack zip itself.
        let zip_path = download_zip_to_temp(&app, &task_id, &download_url, &file_name)?;

        // 2. Extract manifest.json + overrides/ (no mod jars live in the zip).
        fs::create_dir_all(&path).map_err(|error| error.to_string())?;
        extract_curseforge_zip(&zip_path, &path)?;
        let _ = fs::remove_file(&zip_path);

        emit_progress(&app, &task_id, "manifest.json", 0, 1, 0, 1, false);
        let manifest_raw = fs::read(path.join("manifest.json"))
            .map_err(|error| format!("This archive doesn't look like a CurseForge modpack (manifest.json missing): {error}"))?;
        let manifest: CurseManifest = serde_json::from_slice(&manifest_raw)
            .map_err(|error| format!("manifest.json is invalid: {error}"))?;
        emit_progress(&app, &task_id, "manifest.json", 1, 1, 1, 1, true);

        // 3. Resolve + download every mod the manifest references. Best-effort:
        //    a mod whose author disabled third-party downloads shouldn't sink
        //    the whole install -- it's collected into `failed` and reported
        //    back to the user instead.
        let file_total = manifest.files.len();
        let mut failed: Vec<String> = Vec::new();
        for (file_index, entry) in manifest.files.iter().enumerate() {
            match resolve_curseforge_file(entry.project_id, entry.file_id) {
                Ok((url, name, size)) => {
                    let destination = path.join("mods").join(crate::sanitize_relative_path(std::path::Path::new(&name)));
                    if let Err(error) = download_mod_file(&app, &task_id, &url, &destination, &name, file_index, file_total, size) {
                        failed.push(format!("{name}: {error}"));
                    }
                }
                Err(error) => {
                    failed.push(format!("project {} file {}: {error}", entry.project_id, entry.file_id));
                    let _ = entry.required; // required-vs-optional isn't distinguished today; all listed files are attempted.
                }
            }
        }
        emit_progress(&app, &task_id, "mods", file_total, file_total, 1, 1, true);

        // 4. Overrides (configs, resourcepacks, shaderpacks, extra bundled
        //    mods, etc.) get copied up into the instance root.
        emit_progress(&app, &task_id, "overrides", 0, 1, 0, 1, false);
        let overrides_dir = if manifest.overrides.trim().is_empty() { "overrides".to_string() } else { manifest.overrides.clone() };
        merge_overrides(&path, &overrides_dir)?;
        emit_progress(&app, &task_id, "overrides", 1, 1, 1, 1, true);

        // 5. Instance is ready -- write instance.json.
        let (loader_name, loader_version) = manifest.minecraft.mod_loaders.iter()
            .find(|entry| entry.primary)
            .or_else(|| manifest.minecraft.mod_loaders.first())
            .map(|entry| loader_from_curseforge_id(&entry.id))
            .unwrap_or(("Vanilla", None));

        let instance = Instance {
            name: instance_name.clone(),
            minecraft_version: manifest.minecraft.version,
            loader: loader_name.to_string(),
            loader_version,
            created: chrono::Utc::now().to_rfc3339(),
            size: crate::directory_size(&path),
            game_directory: Some(path.to_string_lossy().into_owned()),
            icon_path: icon_url.and_then(|url| crate::download_instance_icon(&url, &path)),
        };
        fs::write(path.join("instance.json"), serde_json::to_string_pretty(&instance).map_err(|error| error.to_string())?)
            .map_err(|error| error.to_string())?;

        Ok(CurseforgeModpackInstallResult { instance, warnings: failed })
    })();

    if result.is_err() {
        // Only a genuinely fatal failure (bad zip, missing manifest.json,
        // no write access, ...) reaches here -- per-mod download failures
        // are collected into `warnings` above and returned as Ok, so the
        // instance folder they'd otherwise leave behind is only cleaned up
        // for real failures.
        let _ = fs::remove_dir_all(&path);
    }
    result
}
