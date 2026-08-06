// Instance content — everything needed for the Instances page's View/Edit
// modals, and the Home page's Worlds/Screenshots tabs (STEP added by user
// request: "видалити мок-контент з Home, додати Світи + Скріншоти;
// додати View/Play/Play With/Edit кнопки на кожен instance").

use serde::{Deserialize, Serialize};
use std::{fs, path::PathBuf};
use tauri::AppHandle;

use crate::{instance_path, instances_dir, project_type_folder};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct InstanceContentSummary {
    mods: Vec<String>,
    resourcepacks: Vec<String>,
    shaderpacks: Vec<String>,
    worlds: Vec<String>,
    screenshots: Vec<String>,
}

fn list_filenames(dir: &PathBuf) -> Vec<String> {
    let Ok(entries) = fs::read_dir(dir) else { return Vec::new() };
    let mut names: Vec<String> = entries
        .filter_map(|entry| entry.ok())
        .filter_map(|entry| entry.file_name().to_str().map(|s| s.to_string()))
        .collect();
    names.sort_by_key(|name| name.to_lowercase());
    names
}

/// Read-only summary of an instance's mods/resourcepacks/shaderpacks/
/// worlds/screenshots — used by both the "View" and "Edit" modals (Edit
/// just adds action buttons on top of the same list).
#[tauri::command]
pub async fn get_instance_content(app: AppHandle, name: String) -> Result<InstanceContentSummary, String> {
    tauri::async_runtime::spawn_blocking(move || get_instance_content_impl(app, name))
        .await
        .map_err(|error| error.to_string())?
}

pub fn get_instance_content_impl(app: AppHandle, name: String) -> Result<InstanceContentSummary, String> {
    let path = instance_path(&app, &name)?;
    Ok(InstanceContentSummary {
        mods: list_filenames(&path.join("mods")),
        resourcepacks: list_filenames(&path.join("resourcepacks")),
        shaderpacks: list_filenames(&path.join("shaderpacks")),
        worlds: list_filenames(&path.join("saves")),
        screenshots: list_filenames(&path.join("screenshots")),
    })
}


/// Removes a single mod/resourcepack/shaderpack file from an instance
/// ("Edit" modal's per-item delete button). `category` is the same
/// vocabulary as install_modrinth_file's project_type: "mod" |
/// "resourcepack" | "shader".
#[tauri::command]
pub async fn remove_instance_file(app: AppHandle, name: String, category: String, filename: String) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || remove_instance_file_impl(app, name, category, filename))
        .await
        .map_err(|error| error.to_string())?
}

pub fn remove_instance_file_impl(app: AppHandle, name: String, category: String, filename: String) -> Result<(), String> {
    let folder = project_type_folder(&category)?;
    let path = instance_path(&app, &name)?;
    let safe_name = PathBuf::from(&filename).file_name().ok_or("Invalid file name.")?.to_owned();
    fs::remove_file(path.join(folder).join(safe_name)).map_err(|error| error.to_string())
}


/// Copies a local file the user picked (browse_local_content_file) into
/// the right instance subfolder ("Edit" modal's "Add..." button) — manual,
/// local-file alternative to installing from Modrinth via Marketplace.
#[tauri::command]
pub async fn add_instance_file(app: AppHandle, name: String, category: String, source_path: String) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || add_instance_file_impl(app, name, category, source_path))
        .await
        .map_err(|error| error.to_string())?
}

pub fn add_instance_file_impl(app: AppHandle, name: String, category: String, source_path: String) -> Result<(), String> {
    let folder = project_type_folder(&category)?;
    let source = PathBuf::from(&source_path);
    if !source.is_file() { return Err("The selected file could not be found.".into()); }
    let target_dir = instance_path(&app, &name)?.join(folder);
    fs::create_dir_all(&target_dir).map_err(|error| error.to_string())?;
    let filename = source.file_name().ok_or("Invalid file name.")?;
    fs::copy(&source, target_dir.join(filename)).map_err(|error| error.to_string())?;
    Ok(())
}


#[tauri::command]
pub async fn browse_local_content_file() -> Result<Option<String>, String> {
    tauri::async_runtime::spawn_blocking(move || browse_local_content_file_impl())
        .await
        .map_err(|error| error.to_string())?
}

pub fn browse_local_content_file_impl() -> Result<Option<String>, String> {
    Ok(rfd::FileDialog::new()
        .set_title("Select a file to add")
        .add_filter("Mods/Packs", &["jar", "zip"])
        .pick_file()
        .map(|path| path.to_string_lossy().into_owned()))
}


// ── Worlds (Home page's "Світи" tab) ──────────────────────────────────────

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WorldEntry {
    instance_name: String,
    world_name: String,
    path: String, // absolute path to saves/<world>, for reference only
}

/// Lists every world (saves/<world> folder) across ALL instances, each
/// tagged with which instance it belongs to. Home has no per-instance
/// selection UI, so this aggregates everything into one flat list.
#[tauri::command]
pub async fn list_all_worlds(app: AppHandle) -> Result<Vec<WorldEntry>, String> {
    tauri::async_runtime::spawn_blocking(move || list_all_worlds_impl(app))
        .await
        .map_err(|error| error.to_string())?
}

pub fn list_all_worlds_impl(app: AppHandle) -> Result<Vec<WorldEntry>, String> {
    let root = instances_dir(&app)?;
    if !root.exists() { return Ok(Vec::new()); }
    let mut result = Vec::new();
    for entry in fs::read_dir(&root).map_err(|error| error.to_string())? {
        let instance_dir = entry.map_err(|error| error.to_string())?.path();
        if !instance_dir.is_dir() { continue; }
        let Some(instance_name) = instance_dir.file_name().and_then(|n| n.to_str()) else { continue };
        let saves_dir = instance_dir.join("saves");
        if !saves_dir.is_dir() { continue; }
        for world_entry in fs::read_dir(&saves_dir).map_err(|error| error.to_string())? {
            let world_path = world_entry.map_err(|error| error.to_string())?.path();
            if !world_path.is_dir() { continue; }
            let Some(world_name) = world_path.file_name().and_then(|n| n.to_str()) else { continue };
            result.push(WorldEntry {
                instance_name: instance_name.to_string(),
                world_name: world_name.to_string(),
                path: world_path.to_string_lossy().to_string(),
            });
        }
    }
    result.sort_by(|a, b| (&a.instance_name, &a.world_name).cmp(&(&b.instance_name, &b.world_name)));
    Ok(result)
}


#[tauri::command]
pub async fn browse_datapack_file() -> Result<Option<String>, String> {
    tauri::async_runtime::spawn_blocking(move || browse_datapack_file_impl())
        .await
        .map_err(|error| error.to_string())?
}

pub fn browse_datapack_file_impl() -> Result<Option<String>, String> {
    Ok(rfd::FileDialog::new()
        .set_title("Select a datapack (.zip)")
        .add_filter("Datapack", &["zip"])
        .pick_file()
        .map(|path| path.to_string_lossy().into_owned()))
}


/// Installs a datapack into a SPECIFIC world (saves/<world>/datapacks/),
/// not the instance-wide "datapacks" folder that install_modrinth_file
/// uses for generic Marketplace installs. Minecraft loads a datapack
/// that's left as a .zip directly in that folder just fine — no need to
/// extract it.
#[tauri::command]
pub async fn install_world_datapack(app: AppHandle, instance_name: String, world_name: String, source_path: String) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || install_world_datapack_impl(app, instance_name, world_name, source_path))
        .await
        .map_err(|error| error.to_string())?
}

pub fn install_world_datapack_impl(app: AppHandle, instance_name: String, world_name: String, source_path: String) -> Result<(), String> {
    let source = PathBuf::from(&source_path);
    if !source.is_file() { return Err("The datapack file could not be found.".into()); }
    let safe_world = PathBuf::from(&world_name).file_name().ok_or("Invalid world name.")?.to_owned();
    let target_dir = instance_path(&app, &instance_name)?.join("saves").join(safe_world).join("datapacks");
    fs::create_dir_all(&target_dir).map_err(|error| error.to_string())?;
    let filename = source.file_name().ok_or("Invalid file name.")?;
    fs::copy(&source, target_dir.join(filename)).map_err(|error| error.to_string())?;
    Ok(())
}


// ── Screenshots (Home page's "Скріншоти" tab) ─────────────────────────────

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ScreenshotEntry {
    instance_name: String,
    filename: String,
    path: String, // absolute path, shown via convertFileSrc() on the frontend
}

/// Lists every screenshot (screenshots/*.png) across ALL instances, each
/// tagged with which instance it came from. Same aggregation reasoning as
/// list_all_worlds above.
///
/// NOTE: showing these images via convertFileSrc() needs the instances
/// folder to be in Tauri's asset-protocol scope — handled by
/// extend_theme_asset_scope() in lib.rs (static conf.json entry for the
/// default data directory, plus dynamic extension for a custom one), same
/// mechanism as theme preview images.
#[tauri::command]
pub async fn list_all_screenshots(app: AppHandle) -> Result<Vec<ScreenshotEntry>, String> {
    tauri::async_runtime::spawn_blocking(move || list_all_screenshots_impl(app))
        .await
        .map_err(|error| error.to_string())?
}

pub fn list_all_screenshots_impl(app: AppHandle) -> Result<Vec<ScreenshotEntry>, String> {
    let root = instances_dir(&app)?;
    if !root.exists() { return Ok(Vec::new()); }
    let mut result = Vec::new();
    for entry in fs::read_dir(&root).map_err(|error| error.to_string())? {
        let instance_dir = entry.map_err(|error| error.to_string())?.path();
        if !instance_dir.is_dir() { continue; }
        let Some(instance_name) = instance_dir.file_name().and_then(|n| n.to_str()) else { continue };
        let screenshots_dir = instance_dir.join("screenshots");
        if !screenshots_dir.is_dir() { continue; }
        for shot_entry in fs::read_dir(&screenshots_dir).map_err(|error| error.to_string())? {
            let shot_path = shot_entry.map_err(|error| error.to_string())?.path();
            if !shot_path.is_file() { continue; }
            let ext = shot_path.extension().and_then(|e| e.to_str()).unwrap_or("").to_lowercase();
            if !["png", "jpg", "jpeg", "webp"].contains(&ext.as_str()) { continue; }
            let Some(filename) = shot_path.file_name().and_then(|n| n.to_str()) else { continue };
            result.push(ScreenshotEntry {
                instance_name: instance_name.to_string(),
                filename: filename.to_string(),
                path: shot_path.to_string_lossy().to_string(),
            });
        }
    }
    // Newest first isn't derivable from the filename alone in general, but
    // Minecraft's own screenshot filenames are timestamp-prefixed, so a
    // plain reverse-alphabetical sort already puts the newest ones first.
    result.sort_by(|a, b| b.filename.cmp(&a.filename));
    Ok(result)
}


/// Root of all instance folders, exposed for lib.rs's asset-scope
/// extension (mirrors theme::themes_root_for_scope's pattern).
pub fn instances_root_for_scope(app: &AppHandle) -> Option<PathBuf> {
    instances_dir(app).ok()
}
