// ResetThemes -- small standalone companion executable for Dream Future
// Launcher. Ships next to the main launcher exe as "ResetThemes.exe" (see
// build-windows.bat) so a user whose active theme has left the UI broken
// (see the sidebar-only layout bug fixed in styles/index.css, or any
// third-party .dftp that ships bad CSS) can get back into a working
// launcher WITHOUT the launcher's own UI needing to be usable at all.
//
// What it does:
//   1. Locates the launcher's data directory the same way the main app
//      does (default AppData/Local location, or the custom location from
//      Settings -> Storage, if one was ever set).
//   2. Resets the active theme back to the bundled default
//      ("Dream Default" / builtin-default) -- or to "no theme
//      active" if even that one is missing from disk somehow.
//   3. If the theme registry file itself is corrupted (invalid JSON), it
//      does NOT just wipe it: it rebuilds the registry by scanning the
//      themes folder on disk and re-reading each installed theme's own
//      manifest.json, so every previously installed/downloaded theme
//      pack stays available in the app afterwards -- only the broken
//      index is repaired, not the theme files themselves.
//
// What it deliberately never touches: instances/, accounts (encrypted or
// otherwise), Java runtimes, the storage-location setting itself, or any
// files inside an individual installed theme's own folder. It only ever
// writes launcher-data/dftp_themes.json (plus a timestamped backup of the
// previous copy).
//
// This is a plain std-only binary (no Tauri runtime) so it can run even
// if the main app's webview/frontend is completely broken.

use std::collections::BTreeMap;
use std::env;
use std::fs;
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

/// Must match `identifier` in src-tauri/tauri.conf.json -- this is how
/// Tauri derives the default per-OS app-local-data folder.
const APP_IDENTIFIER: &str = "com.dreamfuture.launcher";

/// Must match theme.rs's ENGINE_VERSION / default_sidebar_position(); kept
/// as plain literals here since this binary intentionally does not depend
/// on the main crate's (non-pub) theme module.
const DEFAULT_THEME_ID: &str = "builtin-default";
const DEFAULT_SIDEBAR_POSITION: &str = "left";

fn main() {
    println!("Dream Future Launcher — Reset Themes");
    println!("======================================\n");

    match run() {
        Ok(summary) => {
            println!("{summary}\n");
            println!("Done. You can close this window and start the launcher.");
        }
        Err(error) => {
            eprintln!("Could not finish resetting themes: {error}");
            eprintln!("Nothing on disk was changed beyond what's noted above, if anything.");
        }
    }

    pause();
}

fn pause() {
    print!("\nPress Enter to exit...");
    let _ = io::stdout().flush();
    let mut discard = String::new();
    let _ = io::stdin().read_line(&mut discard);
}

fn run() -> Result<String, String> {
    let default_root = default_app_local_data_dir()?;
    let data_root = resolve_data_root(&default_root);

    println!("Launcher data directory: {}", data_root.display());

    let registry_path = data_root.join("launcher-data").join("dftp_themes.json");
    let themes_root = data_root.join("dftp-themes");

    if !registry_path.is_file() {
        return Ok(format!(
            "No theme configuration found at {} — there's nothing to reset. \
             The launcher will use its default theme the next time it starts.",
            registry_path.display()
        ));
    }

    let raw = fs::read_to_string(&registry_path)
        .map_err(|error| format!("Could not read {}: {error}", registry_path.display()))?;

    let (mut registry, rebuilt_from_disk) = match parse_registry(&raw) {
        Some(registry) => (registry, false),
        None => {
            // The registry file itself is not valid JSON. Back it up, then
            // rebuild the theme list from what's actually installed on
            // disk instead of discarding it — nothing gets deleted.
            backup_file(&registry_path)?;
            (rebuild_registry_from_disk(&themes_root), true)
        }
    };

    let had_active = registry.active_id.clone();

    // The whole point of this tool: force the active theme back to the
    // bundled default (or to "none" if even that is missing), every time
    // it's run — regardless of whether the previously active theme looked
    // valid. This never removes anything from the `themes` list.
    let default_present = registry
        .themes
        .iter()
        .any(|theme| theme.id == DEFAULT_THEME_ID)
        && themes_root.join(DEFAULT_THEME_ID).join("manifest.json").is_file();

    registry.active_id = if default_present {
        Some(DEFAULT_THEME_ID.to_string())
    } else {
        None
    };

    // Always back up the previous (parseable) registry before overwriting,
    // even in the non-corrupted path — cheap insurance, and it means this
    // tool is safe to re-run.
    if !rebuilt_from_disk {
        backup_file(&registry_path)?;
    }

    write_registry(&registry_path, &registry)?;

    let theme_count = registry.themes.len();
    let mut summary = String::new();
    if rebuilt_from_disk {
        summary.push_str(&format!(
            "The theme configuration file was corrupted and has been rebuilt from the \
             {theme_count} theme(s) found installed on disk. The broken file was kept as a backup \
             next to it (same folder, \".bak\" suffix).\n"
        ));
    } else {
        summary.push_str(&format!(
            "Theme configuration read successfully ({theme_count} installed theme(s) found, \
             all kept). A backup of the previous configuration was saved next to it.\n"
        ));
    }

    match (&had_active, &registry.active_id) {
        (Some(previous), Some(now)) if previous == now => {
            summary.push_str(&format!("Active theme was already the default (\"{now}\") — left as is.\n"));
        }
        (_, Some(now)) => {
            summary.push_str(&format!("Active theme has been reset to the default (\"{now}\").\n"));
        }
        (_, None) => {
            summary.push_str(
                "Active theme has been cleared (the bundled default theme was not found on disk, \
                 so the launcher will just use its plain built-in look — reinstalling it from \
                 Settings -> Theme Packs is safe and won't affect anything else).\n",
            );
        }
    }

    summary.push_str("No instances, accounts, Java runtimes, or installed/downloaded theme files were touched.");
    Ok(summary)
}

// ── Locating the data directory (mirrors src-tauri/src/lib.rs::data_root) ──

fn default_app_local_data_dir() -> Result<PathBuf, String> {
    #[cfg(target_os = "windows")]
    {
        let base = env::var("LOCALAPPDATA")
            .map_err(|_| "Could not find the %LOCALAPPDATA% environment variable.".to_string())?;
        Ok(PathBuf::from(base).join(APP_IDENTIFIER))
    }
    #[cfg(target_os = "macos")]
    {
        let home = env::var("HOME").map_err(|_| "Could not find the HOME environment variable.".to_string())?;
        Ok(PathBuf::from(home).join("Library").join("Application Support").join(APP_IDENTIFIER))
    }
    #[cfg(all(unix, not(target_os = "macos")))]
    {
        if let Ok(xdg) = env::var("XDG_DATA_HOME") {
            if !xdg.trim().is_empty() {
                return Ok(PathBuf::from(xdg).join(APP_IDENTIFIER));
            }
        }
        let home = env::var("HOME").map_err(|_| "Could not find the HOME environment variable.".to_string())?;
        Ok(PathBuf::from(home).join(".local").join("share").join(APP_IDENTIFIER))
    }
}

/// Mirrors lib.rs's data_root(): the app_config.json override, if present,
/// always lives in the DEFAULT location (it's never itself moved), and
/// points at wherever the user relocated everything else to.
fn resolve_data_root(default_root: &Path) -> PathBuf {
    let config_path = default_root.join("launcher-data").join("app_config.json");
    let Ok(raw) = fs::read_to_string(&config_path) else { return default_root.to_path_buf(); };
    let Some(value) = json_parse(&raw) else { return default_root.to_path_buf(); };
    match value.get_str("customDataDir").or_else(|| value.get_str("custom_data_dir")) {
        Some(dir) if !dir.trim().is_empty() => PathBuf::from(dir.trim()),
        _ => default_root.to_path_buf(),
    }
}

// ── Registry model ──────────────────────────────────────────────────────

struct ThemeRecord {
    id: String,
    name: String,
    author: String,
    version: String,
    background: Option<String>,
    preview: Option<String>,
    has_custom_css: bool,
    sidebar_position: String,
    hidden_tabs: Vec<String>,
    tab_order: Vec<String>,
}

struct Registry {
    themes: Vec<ThemeRecord>,
    active_id: Option<String>,
}

fn parse_registry(raw: &str) -> Option<Registry> {
    let value = json_parse(raw)?;
    let themes_value = value.get("themes")?;
    let mut themes = Vec::new();
    for item in themes_value.as_array()? {
        let id = item.get_str("id")?.to_string();
        themes.push(ThemeRecord {
            id,
            name: item.get_str("name").unwrap_or("Untitled theme").to_string(),
            author: item.get_str("author").unwrap_or("Unknown").to_string(),
            version: item.get_str("version").unwrap_or("1.0.0").to_string(),
            background: item.get_str("background").map(|s| s.to_string()),
            preview: item.get_str("preview").map(|s| s.to_string()),
            has_custom_css: item.get_bool("hasCustomCss").unwrap_or(false),
            sidebar_position: item.get_str("sidebarPosition").unwrap_or(DEFAULT_SIDEBAR_POSITION).to_string(),
            hidden_tabs: item.get_str_array("hiddenTabs"),
            tab_order: item.get_str_array("tabOrder"),
        });
    }
    // NOTE: ThemeRegistry (unlike ThemeRecord) has no #[serde(rename_all =
    // "camelCase")], so the top-level key is snake_case "active_id" even
    // though the per-theme fields nested inside "themes" are camelCase.
    let active_id = value.get("active_id").and_then(|v| v.as_str()).map(|s| s.to_string());
    Some(Registry { themes, active_id })
}

/// Rebuilds a registry purely from what's on disk under `themes_root`: one
/// subfolder per installed theme, each with its own manifest.json (the
/// exact layout theme_install_impl in theme.rs creates). Used only when
/// the registry JSON itself failed to parse.
fn rebuild_registry_from_disk(themes_root: &Path) -> Registry {
    let mut themes = Vec::new();
    let Ok(entries) = fs::read_dir(themes_root) else {
        return Registry { themes, active_id: None };
    };
    for entry in entries.flatten() {
        let folder = entry.path();
        if !folder.is_dir() {
            continue;
        }
        let manifest_path = folder.join("manifest.json");
        let Ok(raw) = fs::read_to_string(&manifest_path) else { continue };
        let Some(manifest) = json_parse(&raw) else { continue };
        let Some(id) = folder.file_name().and_then(|n| n.to_str()) else { continue };

        let background = manifest
            .get_str("background")
            .filter(|file| folder.join(file).is_file())
            .map(|s| s.to_string());
        let preview = manifest
            .get_str("preview")
            .filter(|file| folder.join(file).is_file())
            .map(|s| s.to_string());
        let has_custom_css = folder.join("custom.css").is_file();

        themes.push(ThemeRecord {
            id: id.to_string(),
            name: manifest.get_str("name").unwrap_or("Untitled theme").to_string(),
            author: manifest.get_str("author").unwrap_or("Unknown").to_string(),
            version: manifest.get_str("version").unwrap_or("1.0.0").to_string(),
            background,
            preview,
            has_custom_css,
            sidebar_position: manifest.get_str("sidebarPosition").unwrap_or(DEFAULT_SIDEBAR_POSITION).to_string(),
            hidden_tabs: manifest.get_str_array("hiddenTabs"),
            tab_order: manifest.get_str_array("tabOrder"),
        });
    }
    themes.sort_by(|a, b| a.id.cmp(&b.id));
    Registry { themes, active_id: None }
}

fn write_registry(path: &Path, registry: &Registry) -> Result<(), String> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|error| error.to_string())?;
    }
    let json = registry_to_json(registry);
    fs::write(path, json).map_err(|error| format!("Could not write {}: {error}", path.display()))
}

fn backup_file(path: &Path) -> Result<(), String> {
    if !path.is_file() {
        return Ok(());
    }
    let timestamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    let backup_path = path.with_extension(format!("json.{timestamp}.bak"));
    fs::copy(path, &backup_path)
        .map(|_| ())
        .map_err(|error| format!("Could not back up {} before resetting it: {error}", path.display()))
}

// ── A tiny, dependency-free JSON reader/writer ─────────────────────────
//
// This binary intentionally avoids pulling in serde_json (or any other
// crate) so it stays a small, fast, self-contained tool that's trivial to
// audit — it only ever needs to read a handful of string/bool/array
// fields and write back a known, fixed shape.

#[derive(Debug, Clone)]
enum Json {
    Null,
    Bool(bool),
    String(String),
    Array(Vec<Json>),
    Object(BTreeMap<String, Json>),
}

impl Json {
    fn get(&self, key: &str) -> Option<&Json> {
        match self {
            Json::Object(map) => map.get(key),
            _ => None,
        }
    }
    fn as_str(&self) -> Option<&str> {
        match self {
            Json::String(s) => Some(s),
            _ => None,
        }
    }
    fn as_array(&self) -> Option<&Vec<Json>> {
        match self {
            Json::Array(items) => Some(items),
            _ => None,
        }
    }
    fn get_str(&self, key: &str) -> Option<&str> {
        self.get(key).and_then(Json::as_str)
    }
    fn get_bool(&self, key: &str) -> Option<bool> {
        match self.get(key) {
            Some(Json::Bool(b)) => Some(*b),
            _ => None,
        }
    }
    fn get_str_array(&self, key: &str) -> Vec<String> {
        self.get(key)
            .and_then(Json::as_array)
            .map(|items| items.iter().filter_map(Json::as_str).map(|s| s.to_string()).collect())
            .unwrap_or_default()
    }
}

fn json_parse(input: &str) -> Option<Json> {
    let chars: Vec<char> = input.chars().collect();
    let (value, _end) = parse_value(&chars, skip_ws(&chars, 0))?;
    Some(value)
}

fn skip_ws(chars: &[char], mut pos: usize) -> usize {
    while pos < chars.len() && chars[pos].is_whitespace() {
        pos += 1;
    }
    pos
}

fn parse_value(chars: &[char], pos: usize) -> Option<(Json, usize)> {
    let pos = skip_ws(chars, pos);
    match chars.get(pos)? {
        '{' => parse_object(chars, pos),
        '[' => parse_array(chars, pos),
        '"' => parse_string(chars, pos).map(|(s, end)| (Json::String(s), end)),
        't' if chars[pos..].starts_with(&['t', 'r', 'u', 'e']) => Some((Json::Bool(true), pos + 4)),
        'f' if chars[pos..].starts_with(&['f', 'a', 'l', 's', 'e']) => Some((Json::Bool(false), pos + 5)),
        'n' if chars[pos..].starts_with(&['n', 'u', 'l', 'l']) => Some((Json::Null, pos + 4)),
        c if c.is_ascii_digit() || *c == '-' => parse_number(chars, pos),
        _ => None,
    }
}

fn parse_number(chars: &[char], mut pos: usize) -> Option<(Json, usize)> {
    let start = pos;
    if chars.get(pos) == Some(&'-') {
        pos += 1;
    }
    while pos < chars.len() && (chars[pos].is_ascii_digit() || chars[pos] == '.' || chars[pos] == 'e' || chars[pos] == 'E' || chars[pos] == '+' || chars[pos] == '-') {
        pos += 1;
    }
    let text: String = chars[start..pos].iter().collect();
    // Numbers aren't used by anything this tool reads; keep as a string so
    // Json stays a 4-variant enum.
    Some((Json::String(text), pos))
}

fn parse_string(chars: &[char], pos: usize) -> Option<(String, usize)> {
    if chars.get(pos) != Some(&'"') {
        return None;
    }
    let mut pos = pos + 1;
    let mut out = String::new();
    while let Some(&c) = chars.get(pos) {
        match c {
            '"' => return Some((out, pos + 1)),
            '\\' => {
                pos += 1;
                match chars.get(pos)? {
                    'n' => out.push('\n'),
                    't' => out.push('\t'),
                    'r' => out.push('\r'),
                    '"' => out.push('"'),
                    '\\' => out.push('\\'),
                    '/' => out.push('/'),
                    'u' => {
                        let hex: String = chars.get(pos + 1..pos + 5)?.iter().collect();
                        let code = u32::from_str_radix(&hex, 16).ok()?;
                        out.push(char::from_u32(code).unwrap_or('?'));
                        pos += 4;
                    }
                    other => out.push(*other),
                }
                pos += 1;
            }
            other => {
                out.push(other);
                pos += 1;
            }
        }
    }
    None
}

fn parse_array(chars: &[char], pos: usize) -> Option<(Json, usize)> {
    let mut pos = pos + 1;
    let mut items = Vec::new();
    pos = skip_ws(chars, pos);
    if chars.get(pos) == Some(&']') {
        return Some((Json::Array(items), pos + 1));
    }
    loop {
        let (value, next) = parse_value(chars, pos)?;
        items.push(value);
        pos = skip_ws(chars, next);
        match chars.get(pos)? {
            ',' => {
                pos = skip_ws(chars, pos + 1);
            }
            ']' => return Some((Json::Array(items), pos + 1)),
            _ => return None,
        }
    }
}

fn parse_object(chars: &[char], pos: usize) -> Option<(Json, usize)> {
    let mut pos = pos + 1;
    let mut map = BTreeMap::new();
    pos = skip_ws(chars, pos);
    if chars.get(pos) == Some(&'}') {
        return Some((Json::Object(map), pos + 1));
    }
    loop {
        pos = skip_ws(chars, pos);
        let (key, next) = parse_string(chars, pos)?;
        pos = skip_ws(chars, next);
        if chars.get(pos) != Some(&':') {
            return None;
        }
        pos = skip_ws(chars, pos + 1);
        let (value, next) = parse_value(chars, pos)?;
        map.insert(key, value);
        pos = skip_ws(chars, next);
        match chars.get(pos)? {
            ',' => {
                pos += 1;
            }
            '}' => return Some((Json::Object(map), pos + 1)),
            _ => return None,
        }
    }
}

fn json_escape(input: &str) -> String {
    let mut out = String::with_capacity(input.len());
    for c in input.chars() {
        match c {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            c if (c as u32) < 0x20 => out.push_str(&format!("\\u{:04x}", c as u32)),
            c => out.push(c),
        }
    }
    out
}

fn json_string_array(items: &[String]) -> String {
    let inner = items.iter().map(|s| format!("\"{}\"", json_escape(s))).collect::<Vec<_>>().join(", ");
    format!("[{inner}]")
}

fn registry_to_json(registry: &Registry) -> String {
    let mut out = String::new();
    out.push_str("{\n  \"themes\": [\n");
    for (index, theme) in registry.themes.iter().enumerate() {
        out.push_str("    {\n");
        out.push_str(&format!("      \"id\": \"{}\",\n", json_escape(&theme.id)));
        out.push_str(&format!("      \"name\": \"{}\",\n", json_escape(&theme.name)));
        out.push_str(&format!("      \"author\": \"{}\",\n", json_escape(&theme.author)));
        out.push_str(&format!("      \"version\": \"{}\",\n", json_escape(&theme.version)));
        match &theme.background {
            Some(value) => out.push_str(&format!("      \"background\": \"{}\",\n", json_escape(value))),
            None => out.push_str("      \"background\": null,\n"),
        }
        match &theme.preview {
            Some(value) => out.push_str(&format!("      \"preview\": \"{}\",\n", json_escape(value))),
            None => out.push_str("      \"preview\": null,\n"),
        }
        out.push_str(&format!("      \"hasCustomCss\": {},\n", theme.has_custom_css));
        out.push_str(&format!("      \"sidebarPosition\": \"{}\",\n", json_escape(&theme.sidebar_position)));
        out.push_str(&format!("      \"hiddenTabs\": {},\n", json_string_array(&theme.hidden_tabs)));
        out.push_str(&format!("      \"tabOrder\": {}\n", json_string_array(&theme.tab_order)));
        out.push_str("    }");
        out.push_str(if index + 1 < registry.themes.len() { ",\n" } else { "\n" });
    }
    out.push_str("  ],\n");
    // Top-level key stays snake_case ("active_id") to match ThemeRegistry's
    // serde shape — see the note in parse_registry().
    match &registry.active_id {
        Some(id) => out.push_str(&format!("  \"active_id\": \"{}\"\n", json_escape(id))),
        None => out.push_str("  \"active_id\": null\n"),
    }
    out.push_str("}\n");
    out
}
