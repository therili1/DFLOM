// Theme Engine — installs, lists, activates, and removes packaged ".dftp"
// theme packs. A .dftp is a plain ZIP archive with a manifest.json at its
// root; see docs/DFTP_SPEC.md (or the project ТЗ) for the full field list.
//
// This module owns its own on-disk folder (`dftp-themes/`) and its own
// registry file (`launcher-data/dftp_themes.json`). The older, separate
// "import a raw .css file" system that used to live in lib.rs has been
// removed — a .dftp can now optionally bundle a "custom.css" at its root,
// applied the same way (a <style> tag injected by JS) but packaged
// together with background/preview/fonts instead of as a disconnected
// system.

use serde::{Deserialize, Serialize};
use std::{collections::HashMap, fs, io::Read, io::Write, path::{Path, PathBuf}};
use tauri::AppHandle;
use uuid::Uuid;

use crate::data_root;

/// Strips network-reaching constructs from a theme's custom.css before it's
/// ever written to disk or injected into the app. custom.css only ever ends
/// up as a <style> tag's textContent (never eval'd, never HTML), so it can't
/// run JS — but plain CSS can still "phone home" via `@import` (loads a
/// remote stylesheet the theme author can silently swap out later) or via
/// `url(https://...)` in a background/font/list-style (loads on every
/// launch, leaking the user's IP to whoever the theme author is, and can
/// double as a tracking beacon). This keeps local/relative and data: URLs
/// (the theme's own bundled assets) intact and only removes the
/// network-reaching cases.
fn sanitize_custom_css(css: &str) -> String {
    let mut out = String::with_capacity(css.len());
    let bytes = css.as_bytes();
    let mut i = 0;
    while i < bytes.len() {
        let rest = &css[i..];
        let lower_rest = rest.to_ascii_lowercase();
        if lower_rest.starts_with("@import") {
            // Drop everything up to (and including) the terminating ';', or
            // to the end of the file if the statement is unterminated.
            if let Some(end) = rest.find(';') { i += end + 1; } else { i = bytes.len(); }
            continue;
        }
        if lower_rest.starts_with("url(") {
            if let Some(close) = rest.find(')') {
                let inner = rest[4..close].trim().trim_matches(|c| c == '\'' || c == '"');
                let inner_lower = inner.to_ascii_lowercase();
                let is_remote = inner_lower.starts_with("http://")
                    || inner_lower.starts_with("https://")
                    || inner_lower.starts_with("//");
                if is_remote {
                    out.push_str("url()");
                } else {
                    out.push_str(&rest[..=close]);
                }
                i += close + 1;
                continue;
            }
        }
        // Fall back to copying a single (possibly multi-byte) char at a time.
        let ch = rest.chars().next().unwrap();
        out.push(ch);
        i += ch.len_utf8();
    }
    out
}

/// Same treatment as sanitize_custom_css, applied to every *.css file
/// under a theme's pages/ folder (the "Hybrid CSS" mode — see
/// theme_read_page_css below). Silently does nothing if pages/ doesn't
/// exist. Best-effort: a single unreadable/unwritable file is skipped
/// rather than failing the whole install.
fn sanitize_pages_dir(folder: &Path) {
    let pages_dir = folder.join("pages");
    let Ok(entries) = fs::read_dir(&pages_dir) else { return; };
    for entry in entries.flatten() {
        let path = entry.path();
        if path.extension().and_then(|e| e.to_str()).map(|e| e.eq_ignore_ascii_case("css")) != Some(true) {
            continue;
        }
        if let Ok(raw) = fs::read_to_string(&path) {
            let cleaned = sanitize_custom_css(&raw);
            let _ = fs::write(&path, cleaned);
        }
    }
}

/// Theme Engine version this build supports. A .dftp's manifest.json
/// "engineVersion" must match this exactly, or the install is rejected.
const ENGINE_VERSION: &str = "1.0";

/// Mode this build supports for installable packs. A future "css" mode is
/// planned but not implemented yet — anything other than "engine" (case
/// insensitive) is rejected today.
const SUPPORTED_MODE: &str = "engine";

const SUPPORTED_BACKGROUND_EXTENSIONS: &[&str] = &["png", "jpg", "jpeg", "webp", "gif", "mp4", "webm"];

// ── manifest.json model + validation ─────────────────────────────────────

/// Allowed values for manifest.json's "sidebarPosition" / theme_pack's
/// sidebar_position param. Anything else is rejected.
const SIDEBAR_POSITIONS: &[&str] = &["left", "right", "top", "bottom"];

fn default_sidebar_position() -> String { "left".to_string() }

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ThemeManifest {
    name: String,
    author: String,
    version: String,
    engine_version: String,
    mode: String,
    #[serde(default)]
    background: Option<String>,
    #[serde(default)]
    preview: Option<String>,
    /// Where the sidebar sits: "left" (default), "right", "top", "bottom".
    #[serde(default)]
    sidebar_position: Option<String>,
    /// Nav route keys (e.g. "logs", "accounts" — NOT "/logs") to hide.
    /// "home" cannot be hidden (enforced in validate_manifest).
    #[serde(default)]
    hidden_tabs: Option<Vec<String>>,
    /// Nav route keys in the order they should appear; any route not
    /// listed here keeps its default position, appended after the ones
    /// that are listed.
    #[serde(default)]
    tab_order: Option<Vec<String>>,
}

fn validate_manifest(manifest: &ThemeManifest) -> Result<(), String> {
    if manifest.name.trim().is_empty() { return Err("manifest.json: \"name\" is required.".into()); }
    if manifest.author.trim().is_empty() { return Err("manifest.json: \"author\" is required.".into()); }
    if manifest.version.trim().is_empty() { return Err("manifest.json: \"version\" is required.".into()); }

    if manifest.engine_version != ENGINE_VERSION {
        return Err(format!(
            "This theme needs engine version \"{}\", but this launcher build supports \"{}\".",
            manifest.engine_version, ENGINE_VERSION
        ));
    }
    if !manifest.mode.eq_ignore_ascii_case(SUPPORTED_MODE) {
        return Err(format!("mode \"{}\" is not supported yet — only \"engine\" is implemented.", manifest.mode));
    }
    if let Some(background) = &manifest.background {
        let ext = Path::new(background).extension().and_then(|e| e.to_str()).unwrap_or("").to_lowercase();
        if !SUPPORTED_BACKGROUND_EXTENSIONS.contains(&ext.as_str()) {
            return Err(format!("Unsupported background file type: \".{ext}\""));
        }
    }
    if let Some(position) = &manifest.sidebar_position {
        if !SIDEBAR_POSITIONS.contains(&position.to_lowercase().as_str()) {
            return Err(format!("Invalid sidebarPosition \"{position}\" — must be one of: left, right, top, bottom."));
        }
    }
    if let Some(hidden) = &manifest.hidden_tabs {
        if let Some(error) = locked_tab_error(hidden) {
            return Err(error);
        }
    }
    Ok(())
}

// "home", "settings", and "theme-editor" can never be hidden by a theme --
// hiding either of the latter two would strand the user with no way to
// get back into the layout settings and undo it.
fn is_locked_tab(tab: &str) -> bool {
    tab.eq_ignore_ascii_case("home") || tab.eq_ignore_ascii_case("settings") || tab.eq_ignore_ascii_case("theme-editor")
}

fn locked_tab_error(hidden: &[String]) -> Option<String> {
    hidden.iter().find(|tab| is_locked_tab(tab)).map(|tab| format!("\"{tab}\" cannot be hidden."))
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ThemeRecord {
    id: String,
    name: String,
    author: String,
    version: String,
    #[serde(default)]
    background: Option<String>, // filename, relative to this theme's own folder
    #[serde(default)]
    preview: Option<String>,    // filename, relative to this theme's own folder
    #[serde(default)]
    has_custom_css: bool,       // true if a "custom.css" file was found at the archive root
    #[serde(default = "default_sidebar_position")]
    sidebar_position: String,   // "left" | "right" | "top" | "bottom"
    #[serde(default)]
    hidden_tabs: Vec<String>,   // nav route keys to hide, e.g. ["logs","accounts"]
    #[serde(default)]
    tab_order: Vec<String>,     // nav route keys in desired order
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
struct ThemeRegistry {
    #[serde(default)]
    themes: Vec<ThemeRecord>,
    #[serde(default)]
    active_id: Option<String>,
}

/// Public shape handed to the frontend — absolute, ready-to-use paths
/// instead of the registry's relative filenames.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ThemeInfo {
    id: String,
    name: String,
    author: String,
    version: String,
    background_path: Option<String>,
    preview_path: Option<String>,
    is_active: bool,
    has_custom_css: bool,
    sidebar_position: String,
    hidden_tabs: Vec<String>,
    tab_order: Vec<String>,
}

fn themes_root(app: &AppHandle) -> Result<PathBuf, String> {
    // NOTE: background/preview files under this folder are shown in the
    // frontend via Tauri's asset protocol (convertFileSrc()). The static
    // scope entry in tauri.conf.json ("$APPLOCALDATA/dftp-themes/**") only
    // covers the DEFAULT data directory. To also support a custom data
    // directory (Settings -> Storage location), lib.rs additionally
    // extends the asset-protocol scope at runtime via
    // themes_root_for_scope() below — see extend_theme_asset_scope() in
    // lib.rs, called both at startup and right after set_data_directory.
    data_root(app).map(|path| path.join("dftp-themes"))
}

/// Public wrapper so lib.rs can extend the asset-protocol scope to
/// wherever the themes folder currently lives, without needing themes_root
/// itself to be pub (it stays module-private for everything else).
///
/// ⚠️ NOT verified offline: this assumes `AppHandle`/`App` expose
/// `.asset_protocol_scope()` returning something with `.allow_directory()`
/// (Tauri 2's mechanism for permitting paths outside the static
/// tauri.conf.json scope at runtime). If that method/type name is wrong,
/// only the code in lib.rs that calls this needs to change — this
/// function itself will still compile and still returns the right path
/// either way.
pub fn themes_root_for_scope(app: &AppHandle) -> Option<PathBuf> {
    themes_root(app).ok()
}

fn registry_file(app: &AppHandle) -> Result<PathBuf, String> {
    data_root(app).map(|path| path.join("launcher-data").join("dftp_themes.json"))
}

fn read_registry(app: &AppHandle) -> Result<ThemeRegistry, String> {
    let path = registry_file(app)?;
    Ok(fs::read_to_string(path).ok().and_then(|text| serde_json::from_str(&text).ok()).unwrap_or_default())
}

fn write_registry(app: &AppHandle, registry: &ThemeRegistry) -> Result<(), String> {
    let path = registry_file(app)?;
    if let Some(parent) = path.parent() { fs::create_dir_all(parent).map_err(|e| e.to_string())?; }
    fs::write(path, serde_json::to_string_pretty(registry).map_err(|e| e.to_string())?).map_err(|e| e.to_string())
}

fn to_theme_info(record: &ThemeRecord, folder: &Path, active_id: &Option<String>) -> ThemeInfo {
    ThemeInfo {
        id: record.id.clone(),
        name: record.name.clone(),
        author: record.author.clone(),
        version: record.version.clone(),
        background_path: record.background.as_ref().map(|f| folder.join(f).to_string_lossy().to_string()),
        preview_path: record.preview.as_ref().map(|f| folder.join(f).to_string_lossy().to_string()),
        is_active: active_id.as_deref() == Some(record.id.as_str()),
        has_custom_css: record.has_custom_css,
        sidebar_position: record.sidebar_position.clone(),
        hidden_tabs: record.hidden_tabs.clone(),
        tab_order: record.tab_order.clone(),
    }
}

// ── ZIP extraction — same pattern as lib.rs's extract_mrpack ────────────

fn extract_dftp(source: &Path, destination: &Path) -> Result<(), String> {
    let file = fs::File::open(source).map_err(|error| error.to_string())?;
    let mut archive = zip::ZipArchive::new(file).map_err(|_| "The .dftp file is corrupted or not a valid archive.".to_string())?;
    for index in 0..archive.len() {
        let mut entry = archive.by_index(index).map_err(|error| error.to_string())?;
        let Some(entry_path) = entry.enclosed_name().map(|path| path.to_owned()) else {
            let _ = fs::remove_dir_all(destination);
            return Err("The .dftp archive contains an unsafe path.".into());
        };
        let out_path = destination.join(entry_path);
        if entry.is_dir() {
            fs::create_dir_all(&out_path).map_err(|error| error.to_string())?;
        } else {
            if let Some(parent) = out_path.parent() { fs::create_dir_all(parent).map_err(|error| error.to_string())?; }
            let mut output = fs::File::create(&out_path).map_err(|error| error.to_string())?;
            std::io::copy(&mut entry, &mut output).map_err(|error| error.to_string())?;
        }
    }
    Ok(())
}

fn read_manifest_from_zip(source: &Path) -> Result<ThemeManifest, String> {
    let file = fs::File::open(source).map_err(|error| error.to_string())?;
    let mut archive = zip::ZipArchive::new(file).map_err(|_| "The .dftp file is corrupted or not a valid archive.".to_string())?;
    let mut entry = archive.by_name("manifest.json").map_err(|_| "manifest.json not found at the root of the .dftp archive.".to_string())?;
    let mut contents = String::new();
    entry.read_to_string(&mut contents).map_err(|error| error.to_string())?;
    serde_json::from_str(&contents).map_err(|error| format!("manifest.json is not valid: {error}"))
}

// ── Built-in themes, seeded once on first run ────────────────────────────
//
// Three themes ship with the launcher out of the box -- deliberately just
// three GOOD ones instead of a bigger pile of mediocre ones (more can be
// added in later updates once each is actually polished):
//   1. "Dream Default" -- no custom.css, no background, no fonts.
//      Installed AND activated by default: a fresh install should look
//      simple and readable, not like someone else's idea of "cool"
//      before the user ever asked for it. It also doubles as the
//      reference example of a minimal, "correctly structured" theme.
//   2. "Aurora Violet" -- glassy/gradient reskin with its own background
//      image + preview.
//   3. "Північне Сяйво Neon" -- punchier neon/cyberpunk reskin, own
//      background image + preview.
// All three are installed (so they show up immediately in Settings ->
// Theme Packs / Theme Editor) but only #1 is activated -- the user picks
// a different one on purpose if they want it.
//
// Two more themes exist in this file but are intentionally NOT seeded:
//   - VIDEO_SHOWCASE_CSS (own background.mp4) -- video backgrounds are
//     demoed via the downloadable template's background.mp4 instead of
//     a 4th installed theme; the engine support is real and unchanged
//     either way.
//   - DEV_EXAMPLE_CSS ("Developer Theme Example") -- deliberately NOT
//     pretty (every region gets a loud, different color), so it's only
//     reachable via theme_download_dev_example() (a button in Theme
//     Maker), not dropped into a normal user's theme list where it'd
//     just look broken.
//
// seed_builtin_themes only runs once: if the registry already has ANY
// themes in it (from a previous run, or because the user removed the
// built-ins on purpose), it does nothing. Safe to call on every startup.
pub fn seed_builtin_themes(app: &AppHandle) {
    let Ok(mut registry) = read_registry(app) else { return; };
    if !registry.themes.is_empty() {
        return;
    }
    let Ok(root) = themes_root(app) else { return; };

    struct Builtin {
        id: &'static str,
        name: &'static str,
        css: Option<&'static str>,
        background: Option<(&'static str, &'static [u8])>,
        preview: Option<&'static [u8]>,
    }

    let builtins: [Builtin; 3] = [
        Builtin { id: "builtin-default", name: "Dream Default", css: None, background: None, preview: None },
        Builtin {
            id: "builtin-aurora", name: "Aurora Violet", css: Some(AURORA_VIOLET_CSS),
            background: Some(("background.png", include_bytes!("../assets/builtin-themes/aurora/background.png"))),
            preview: Some(include_bytes!("../assets/builtin-themes/aurora/preview.png")),
        },
        Builtin {
            id: "builtin-neon", name: "Північне Сяйво Neon", css: Some(MIDNIGHT_NEON_CSS),
            background: Some(("background.png", include_bytes!("../assets/builtin-themes/neon/background.png"))),
            preview: Some(include_bytes!("../assets/builtin-themes/neon/preview.png")),
        },
    ];

    for builtin in builtins {
        let folder = root.join(builtin.id);
        if fs::create_dir_all(&folder).is_err() {
            continue;
        }
        let has_custom_css = if let Some(css) = builtin.css {
            fs::write(folder.join("custom.css"), css).is_ok()
        } else {
            false
        };
        let background = builtin.background.and_then(|(filename, bytes)| {
            fs::write(folder.join(filename), bytes).ok().map(|_| filename.to_string())
        });
        let preview = builtin.preview.and_then(|bytes| {
            fs::write(folder.join("preview.png"), bytes).ok().map(|_| "preview.png".to_string())
        });
        registry.themes.push(ThemeRecord {
            id: builtin.id.to_string(),
            name: builtin.name.to_string(),
            author: "Dream Future Launcher".to_string(),
            version: "1.0.0".to_string(),
            background,
            preview,
            has_custom_css,
            sidebar_position: default_sidebar_position(),
            hidden_tabs: Vec::new(),
            tab_order: Vec::new(),
        });
    }
    registry.active_id = Some("builtin-default".to_string());
    let _ = write_registry(app, &registry);
}

/// Soft glassy/gradient reskin. Ships its own background.png (see
/// seed_builtin_themes) -- custom.css only needs to handle the UI chrome
/// on top of it (sidebar, cards, buttons), not the window background
/// itself.
const AURORA_VIOLET_CSS: &str = r#"/* ============================================================
   AURORA VIOLET -- вбудована тема Dream Future Launcher
   ============================================================
   Приклад того, як custom.css + власний background.png разом можуть
   ПОВНІСТЮ змінити вигляд лаунчера, не займаючи layout (розташування
   елементів) -- тільки кольори, фони, тіні, розмиття і межі. Дивись
   коментарі нижче по кожному блоку, якщо робиш власну тему -- це той
   самий підхід, що і в шаблоні з кнопки "Download theme template" в
   Theme Maker.

   Фон вікна (нічне фіолетове небо) НЕ намальований тут у CSS -- це
   окремий файл background.png, підключений через manifest.json
   ("background": "background.png"). Theme Engine малює його сам,
   на весь екран, за сайдбаром і контентом -- custom.css відповідає
   лише за те, що поверх нього (скляні панелі, кнопки, підсвітка). */

/* ===========================
   SIDEBAR
   =========================== */
/* Скляний ефект: напівпрозорий фон + розмиття того, що за ним (видно
   фонове зображення крізь сайдбар) */
.sidebar {
  background: rgba(28, 22, 54, 0.55);
  backdrop-filter: blur(18px);
  border-right: 1px solid rgba(150, 130, 255, 0.18);
}
/* Активний пункт меню -- м'яке фіолетове світіння замість плаского фону */
.nav-item.active {
  background: linear-gradient(100deg, rgba(126, 100, 255, 0.28), rgba(90, 70, 200, 0.16));
  box-shadow: inset 2px 0 var(--accent), 0 0 18px rgba(126, 100, 255, 0.25);
}
.nav-item:hover { background: rgba(150, 130, 255, 0.1); }

/* ===========================
   TOPBAR
   =========================== */
.topbar {
  background: rgba(20, 16, 38, 0.4);
  backdrop-filter: blur(14px);
  border-bottom: 1px solid rgba(150, 130, 255, 0.15);
}

/* ===========================
   CARDS / PANELS
   =========================== */
/* Один селектор одразу на всі типи карток у лаунчері (dashboard,
   інстанси, java, маркетплейс, акаунти, налаштування завантажень) */
.panel, .feature-card, .settings-card, .java-card, .managed-instance-card,
.project-card, .account-card, .download-builder, .download-queue,
.launch-debug-builder, .launch-debug-panel {
  background: rgba(30, 24, 56, 0.5) !important;
  border-color: rgba(150, 130, 255, 0.2) !important;
  backdrop-filter: blur(10px);
}

/* ===========================
   HOME HERO
   =========================== */
.hero-card {
  background: linear-gradient(120deg, #241c47 0%, #1c2550 55%, #142238 100%);
  border-color: rgba(150, 130, 255, 0.25);
}

/* ===========================
   BUTTONS
   =========================== */
.primary-button:hover { box-shadow: 0 10px 28px rgba(126, 100, 255, 0.35); }
"#;

/// Punchier neon/cyberpunk reskin. Ships its own background.png (dark
/// with cyan/magenta glow blobs) -- see comment on AURORA_VIOLET_CSS
/// above for why the window background itself isn't in this CSS.
const MIDNIGHT_NEON_CSS: &str = r#"/* ============================================================
   ПІВНІЧНЕ СЯЙВО NEON -- вбудована тема Dream Future Launcher
   ============================================================
   Другий приклад "повної" зміни вигляду: різкіші кути, неонові межі,
   моно-шрифт для заголовків, і власне фонове зображення (див.
   manifest.json "background": "background.png"). Помітно сильніше
   відрізняється від стандартного вигляду, ніж Aurora Violet. */

/* ===========================
   SIDEBAR
   =========================== */
.sidebar {
  background: #070a10;
  border-right: 1px solid #1fd9c766;
  box-shadow: 4px 0 24px #05131155;
}
.nav-item.active {
  background: #0d1a1a;
  box-shadow: inset 2px 0 #1fd9c7, 0 0 14px #1fd9c74d;
  color: #baffef !important;
}
.nav-item:hover { color: #baffef; background: #0d1414; }
.nav-item.active svg { color: #1fd9c7 !important; }

/* ===========================
   TOPBAR
   =========================== */
.topbar { border-bottom: 1px solid #1fd9c74d; background: #06090d; }
.breadcrumb strong { color: #baffef; }

/* ===========================
   CARDS / PANELS
   =========================== */
/* Гострі кути (без border-radius), тонка неонова межа замість
   звичайної темно-сірої */
.panel, .feature-card, .settings-card, .java-card, .managed-instance-card,
.project-card, .account-card, .download-builder, .download-queue,
.launch-debug-builder, .launch-debug-panel, .modal-card {
  background: #070b10 !important;
  border: 1px solid #1fd9c74d !important;
  border-radius: 4px !important;
  box-shadow: none !important;
}

/* ===========================
   HOME HERO
   =========================== */
.hero-card {
  background: linear-gradient(120deg, #06110f 0%, #0a1420 100%);
  border: 1px solid #1fd9c74d;
  border-radius: 6px;
}
.hero-copy h1 em { color: #1fd9c7; }

/* ===========================
   BUTTONS
   =========================== */
.primary-button, .secondary-button, button { border-radius: 4px !important; }
.primary-button:hover { box-shadow: 0 0 20px #1fd9c799; }

/* Заголовки -- моно-шрифт для "терміналового" відчуття */
h1, h2, h3, .breadcrumb strong { font-family: "DM Mono", monospace; letter-spacing: -0.5px; }
"#;

/// Showcases a VIDEO background (background.mp4, looping/muted, handled
/// entirely by the engine -- see MainLayout.tsx's <video> branch). The
/// CSS here is deliberately light: just enough transparency on the UI
/// chrome that the moving background stays visible through it, which is
/// the whole point of the demo.
const VIDEO_SHOWCASE_CSS: &str = r#"/* ============================================================
   ВІДЕОФОН: ПЛИННІ БАРВИ -- вбудована тема Dream Future Launcher
   ============================================================
   Ця тема демонструє відеофон: manifest.json вказує
   "background": "background.mp4", і Theme Engine сам вирішує, що це
   відео (за розширенням файлу) і рендерить його як зациклене,
   беззвучне <video> на весь екран замість статичного <img>.
   custom.css тут майже нічого не робить із самим фоном -- лише
   робить сайдбар/топбар/картки трохи прозорими, щоб відео було
   видно крізь інтерфейс. */

/* ===========================
   SIDEBAR
   =========================== */
.sidebar { background: rgba(8, 14, 18, 0.45); backdrop-filter: blur(20px); border-right: 1px solid rgba(255,255,255,0.08); }

/* ===========================
   TOPBAR
   =========================== */
.topbar { background: rgba(8, 14, 18, 0.35); backdrop-filter: blur(16px); border-bottom: 1px solid rgba(255,255,255,0.08); }

/* ===========================
   CARDS / PANELS
   =========================== */
.panel, .feature-card, .settings-card, .java-card, .managed-instance-card,
.project-card, .account-card, .download-builder, .download-queue,
.launch-debug-builder, .launch-debug-panel {
  background: rgba(10, 16, 20, 0.55) !important;
  backdrop-filter: blur(12px);
  border-color: rgba(255,255,255,0.08) !important;
}

/* ===========================
   HOME HERO
   =========================== */
.hero-card { background: rgba(10, 16, 20, 0.4); backdrop-filter: blur(8px); }
"#;

/// NOT meant to look good -- meant to be a map. Nearly every major
/// region of the UI gets a loud, distinct, saturated color so a theme
/// author (human or AI) can open Settings -> Theme Packs, activate this,
/// click through every page, and immediately see which selector paints
/// which part of the screen. Start from a COPY of this file's structure
/// when building a real theme, then replace the loud colors with your
/// own palette.
const DEV_EXAMPLE_CSS: &str = r#"/* ============================================================
   DEVELOPER THEME EXAMPLE -- еталон структури для авторів тем
   ============================================================
   Кожен блок нижче -- окрема, гучна, легко впізнавана область
   інтерфейсу. Активуй цю тему, пройдись по всіх сторінках лаунчера
   (Home, Instances, Marketplace, Downloads, Settings, Accounts,
   Logs) і подивись, який колір де саме з'являється -- так ти
   візуально "мапуєш" весь інтерфейс за 2 хвилини. */

/* ===========================
   APP SHELL / BACKGROUND
   =========================== */
.app-shell { background: #2b0000; }

/* ===========================
   SIDEBAR
   =========================== */
.sidebar { background: #003300; border-right: 4px solid #00ff00; }
.nav-item { color: #66ff66; }
.nav-item:hover { background: #005500; color: #ffffff; }
.nav-item.active { background: #009900; color: #000000 !important; box-shadow: inset 4px 0 #00ff00; }
.nav-item.active svg { color: #000000 !important; }
.sidebar-footer { background: #002200; border-color: #00ff00; }

/* ===========================
   TOPBAR
   =========================== */
.topbar { background: #330033; border-bottom: 4px solid #ff00ff; }
.breadcrumb strong { color: #ff66ff; }
.icon-button { color: #ff00ff; }
.profile { color: #ff99ff; }

/* ===========================
   BUTTONS
   =========================== */
.primary-button { background: #0000ff !important; color: #ffffff !important; border-radius: 0 !important; }
.secondary-button, button:not(.primary-button) { background: #000099 !important; color: #99ccff !important; border-color: #3366ff !important; border-radius: 0 !important; }

/* ===========================
   HOME HERO
   =========================== */
.hero-card { background: #333300 !important; border: 4px solid #ffff00 !important; }
.hero-copy h1 { color: #ffff00; }
.hero-copy h1 em { color: #ff9900; }
.hero-glow { background: #ffff0033 !important; }

/* ===========================
   CARDS / PANELS (усі типи разом -- одна і та ж гучна рожева межа)
   =========================== */
.panel, .feature-card, .settings-card, .java-card, .managed-instance-card,
.project-card, .account-card, .download-builder, .download-queue,
.launch-debug-builder, .launch-debug-panel, .modal-card {
  background: #1a001a !important;
  border: 3px solid #ff0099 !important;
  border-radius: 0 !important;
}

/* ===========================
   INPUTS / FORMS
   =========================== */
input, select, textarea { background: #001a33 !important; border-color: #0099ff !important; color: #99ddff !important; }

/* ===========================
   STATUS / ACCENTS
   =========================== */
.status-dot { background: #ff6600 !important; box-shadow: 0 0 8px #ff6600 !important; }
.download-status.completed { color: #00ff00 !important; }
.download-status.failed { color: #ff0000 !important; }
"#;

// ── Theme template download (for theme authors) ──────────────────────────
//
// Theme Maker only lets you pick already-existing files (background,
// fonts, custom.css) -- it doesn't teach anyone the .dftp format itself.
// This command packs a ready-to-install .dftp that IS the documentation:
// a valid manifest.json, a custom.css full of comments showing exactly
// which selectors/classes to override for which part of the UI, and a
// README.txt walking through the whole format. Extract it, edit
// custom.css, re-zip (or just re-run Theme Maker with it), rename to
// .dftp, install via Settings -> Theme Packs.
#[tauri::command]
pub async fn theme_download_template() -> Result<String, String> {
    tauri::async_runtime::spawn_blocking(move || theme_download_template_impl())
        .await
        .map_err(|error| error.to_string())?
}

const TEMPLATE_README: &str = r#"# Dream Future Launcher -- шаблон теми (.dftp)

Цей файл сам є встановлюваною темою -- відкрий Settings -> Theme Packs
і онови кнопкою "Install", щоб побачити його в дії (сірий фон-плейсхолдер
із сіткою і своя тестова смужка під топбаром), а тоді редагуй.

## Що таке .dftp

Звичайний ZIP-архів із розширенням `.dftp`. У цьому шаблоні:

```
manifest.json     -- обов'язковий опис теми
README.md         -- цей файл (не впливає на тему, просто документація)
custom.css        -- весь вигляд лаунчера: кольори, фони, тіні, шрифти
background.png    -- приклад фонового зображення
background.mp4    -- приклад ВІДЕОФОНУ (див. розділ "Відеофон" нижче)
preview.png        -- мініатюра, яку видно в списку тем
fonts/README.txt  -- як пакувати власні шрифти
icons/README.txt  -- статус підтримки власних іконок
```

## Як зробити свою тему -- 2 способи

1. **Найпростіше:** відкрий Theme Maker в лаунчері, заповни поля, обери
   background/preview/custom.css (можеш почати з файлів цього шаблону) --
   лаунчер сам збере валідний `.dftp`. Там же є кнопка
   "Download theme template", яка й згенерувала цей архів.
2. **Вручну:** відредагуй `manifest.json` і `custom.css`, заархівуй ВСІ
   файли (не папку, а їхній вміст, тобто `manifest.json` має лежати в
   корені zip-а, а не в підпапці) у `.zip`, перейменуй розширення на
   `.dftp`, встанови через Settings -> Theme Packs.

## Поля manifest.json (буквально всі, що підтримує Theme Engine зараз)

| Поле              | Обов'язкове | Значення |
|-------------------|-------------|----------|
| `name`            | так | назва теми (текст) |
| `author`          | так | автор (текст) |
| `version`         | так | версія, будь-який текст, напр. `"1.0.0"` |
| `engineVersion`   | так | ЗАВЖДИ `"1.0"` для цього білда лаунчера -- інше значення відхиляється при встановленні |
| `mode`            | так | ЗАВЖДИ `"engine"` -- єдиний підтримуваний режим зараз |
| `background`      | ні | ім'я файлу фону в архіві -- `"background.png"` АБО `"background.mp4"` (див. "Відеофон") |
| `preview`         | ні | ім'я файлу мініатюри, напр. `"preview.png"` |
| `sidebarPosition` | ні | `"left"` \| `"right"` \| `"top"` \| `"bottom"` |
| `hiddenTabs`      | ні | масив ключів вкладок, які треба сховати (див. нижче) |
| `tabOrder`        | ні | масив ключів вкладок у потрібному порядку; ключі, яких немає в списку, додаються в кінець |

Ключі вкладок (для `hiddenTabs` / `tabOrder`):
`home` `instances` `marketplace` `downloads` `theme-maker` `theme-editor`
`ai-helper` `logs` `settings` `accounts`

**`ai-helper`** з'являється в лаунчері (і, відповідно, лише тоді має сенс
у `hiddenTabs`/`tabOrder`/`pages/ai-helper.css`) тільки коли користувач
сам увімкнув AI Helper і зберіг API-ключ у Settings -- якщо цих умов
нема, вкладки просто нема, ховати/показувати через `hiddenTabs` нічого
не змінить.

**Важливо:** `"home"`, `"settings"` і `"theme-editor"` НІКОЛИ не можна
сховати -- лаунчер це просто проігнорує, навіть якщо вони є в
`hiddenTabs`, щоб користувач завжди міг повернутись і виправити тему,
якщо щось зламалось.

## Кольори -- як їх міняти

У Theme Engine немає окремого "палітри" поля в manifest.json -- кольори
змінюються прямим CSS у `custom.css` (`background`, `color`,
`border-color` і т.д. на конкретних селекторах). Акцентний колір
інтерфейсу (`--accent`) користувач вибирає сам у Settings і його не
можна перевизначити з теми -- це свідоме обмеження, щоб акцент завжди
залишався під контролем користувача.

## Background -- як він працює

`background` у manifest.json задає ОДИН файл. Підтримувані формати:
`png jpg jpeg webp gif` (статичне зображення) та `mp4 webm` (відео).
Theme Engine сам визначає, яке це зображення чи відео, за
розширенням файлу -- нічого додатково вказувати не треба.

Якщо в архіві лежать одразу і `background.png`, і `background.mp4` (як
у цьому шаблоні -- для прикладу обох варіантів), реально
використовується лише той файл, на який вказує поле `"background"` в
manifest.json. Другий файл просто ігнорується Theme Engine (хоча й
залишиться в архіві -- це не помилка, просто зайва вага). Пріоритету
"якщо є відео, використати відео" не існує -- explicit виграє завжди.

## Відеофон

Постав `"background": "background.mp4"` в manifest.json. Відео
рендериться на весь екран, зациклено, беззвучно, позаду сайдбару й
контенту (так само, як і статичне зображення). Обмеження: сам
Theme Engine не стискає/оптимізує відео за тебе -- завеликий файл
(десятки-сотні МБ) сповільнить встановлення теми та збільшить розмір
`.dftp`; тримайся в межах кількох МБ, коротка петля (3-8 секунд) з
малим дозволом (720p і нижче) виглядає так само добре, як і важка.

## Preview

`preview` -- окреме мініатюрне зображення (рекомендовано ~480x270),
яке показується в картці теми в списку "Theme Packs" / Theme Editor.
Це НЕ обов'язково має бути скріншот -- підійде будь-яке промо-зображення.
Якщо `preview` не вказано, картка теми просто без мініатюри.

## CSS-режими: Standard vs Hybrid

**Standard Mode** (як і раніше) -- один `custom.css` в корені архіву,
змінює весь лаунчер. Ні до чого додаткового вдаватись не треба, і всі
старі теми з одним `custom.css` продовжують працювати без жодних змін.

**Hybrid Mode** (новий, повністю опціональний) -- поруч із `custom.css`
можна додати папку `pages/` з окремим `.css` на кожну вкладку:

```
pages/
  home.css
  instances.css
  marketplace.css
  downloads.css
  settings.css
  accounts.css
  logs.css
  theme-maker.css
  theme-editor.css
  sidebar.css     -- діє ЗАВЖДИ (глобальна "рама", не прив'язана до сторінки)
  topbar.css      -- діє ЗАВЖДИ (те саме)
```

Ім'я файлу = ключ вкладки (той самий, що в `hiddenTabs`/`tabOrder`).
Наявність папки й файлів визначається автоматично -- НІЧОГО вказувати в
manifest.json не треба. Немає файлу для якоїсь вкладки -- просто нічого
додаткового для неї не підключається, помилки не буде.

Порядок підключення: `custom.css` -> `pages/sidebar.css` ->
`pages/topbar.css` -> `pages/<поточна-вкладка>.css`. Тобто файл
конкретної сторінки підключається останнім і може перебити те, що
задав `custom.css` чи sidebar/topbar.

Цей шаблон включає 3 приклади (`pages/sidebar.css`, `pages/topbar.css`,
`pages/home.css`) -- скопіюй той самий підхід для будь-якої іншої
вкладки. Можна взагалі не використовувати `pages/` -- тоді все як
раніше, лише `custom.css`.


Дивись `fonts/README.txt` в цьому архіві -- коротко: файли з `fonts/`
пакуються в `.dftp` і розпаковуються разом з темою, але підключення
через `@font-face` треба прописати самому в `custom.css` (автоматичного
підключення поки що немає).

## Іконки

Дивись `icons/README.txt` в цьому архіві -- коротко: заміна іконок
(логотип, іконки вкладок сайдбару) ЩЕ НЕ підтримується Theme Engine у
цій версії. Папка `icons/` тут лише як заготовка на майбутнє -- зараз
вона нічого не робить.

## Обмеження (для безпеки користувача, застосовуються при встановленні)

- `@import` у `custom.css` вирізається автоматично.
- `url(https://...)` у `custom.css` теж вирізається (лишається порожній
  `url()`) -- це щоб тема не могла "дзвонити додому" при кожному
  запуску лаунчера. Локальні `url()` (напр. `fonts/MyFont.ttf`) не
  чіпаються.
- Тема НЕ може виконувати JavaScript -- тільки CSS + медіа-файли.

## Як протестувати тему

1. Settings -> Theme Packs -> Install -> обери свій `.dftp`.
2. Активуй тему.
3. Пройдись по ВСІХ сторінках (Home, Instances, Marketplace, Downloads,
   Settings, Accounts, Logs) -- легко забути перевірити ту, яку рідко
   відкриваєш, і саме там лишається неідеальний контраст чи зламана
   картка.
4. Постав активним і темний, і світлий акцентний колір користувача
   (Settings) -- переконайся, що текст читається на обох.
"#;

/// The launcher's actual production stylesheet, embedded at compile time.
/// Giving the AI the REAL, uncommented CSS (every class the UI actually
/// uses, with its current values) instead of just the ~15 example
/// selectors in TEMPLATE_CUSTOM_CSS below is what makes generated themes
/// look thorough and intentional instead of generic/weak -- the model can
/// see exactly what it's overriding (marketplace grid, instance cards,
/// modals, pills, progress bars, forms, etc.) instead of guessing class
/// names beyond the handful the template happens to show.
const LIVE_APP_CSS: &str = include_str!("../../src/styles/index.css");

const TEMPLATE_MANIFEST: &str = r#"{
  "name": "Моя нова тема",
  "author": "Твоє ім'я",
  "version": "1.0.0",
  "engineVersion": "1.0",
  "mode": "engine",
  "background": "background.png",
  "preview": "preview.png",
  "sidebarPosition": "left",
  "hiddenTabs": [],
  "tabOrder": []
}
"#;

const TEMPLATE_FONTS_README: &str = r#"Папка fonts/ -- власні шрифти

Клади сюди .ttf / .otf файли -- вони пакуються всередину .dftp разом
з рештою теми і розпаковуються на диск користувача при встановленні.

АЛЕ: Theme Engine НЕ підключає їх автоматично. Треба самому додати
@font-face у custom.css, наприклад:

  @font-face {
    font-family: "MyThemeFont";
    src: url("fonts/MyThemeFont.ttf");
  }

  h1, h2, h3 {
    font-family: "MyThemeFont", sans-serif;
  }

Шлях у url() -- відносно кореня встановленої теми, тобто просто
"fonts/ІмяФайлу.ttf", без "./" чи "../".

Якщо fonts/ порожня -- це нормально, тему можна пакувати й без неї.
"#;

const TEMPLATE_ICONS_README: &str = r#"Папка icons/ -- ЗАРЕЗЕРВОВАНО НА МАЙБУТНЄ

Заміна іконок (логотип лаунчера, іконки вкладок сайдбару, іконку
застосунку) ЩЕ НЕ підтримується поточною версією Theme Engine.

Ця папка тут навмисно порожня -- лишена як місце, куди покласти власні
іконки ЗАЗДАЛЕГІДЬ, якщо/коли підтримка з'явиться в майбутньому
оновленні лаунчера. Прямо зараз файли тут ні на що не впливають.

Якщо хочеш змінити колір/вигляд іконок навігації вже сьогодні -- це
частково можливо через CSS-фільтри в custom.css, напр.:

  .nav-item svg { color: #1fd9c7; }

(іконки сайдбару -- це inline SVG, тому колір міняється через `color`,
не через background-image чи заміну файлу).
"#;

const TEMPLATE_CUSTOM_CSS: &str = r#"/* ============================================================
   ШАБЛОН custom.css -- Dream Future Launcher Theme Engine
   ============================================================
   Це просто CSS, вставляється в кінець <head> лаунчера як звичайний
   <style> ПІСЛЯ вбудованих стилів -- тому однакові за специфічністю
   селектори (напр. .sidebar) переб'ють стандартний вигляд.
   Розкоментовуй/редагуй блоки нижче, щоб міняти конкретні частини
   інтерфейсу. Нічого редагувати не зобов'язково -- можеш почати з
   одного блоку і поступово додавати інші. Повний опис формату --
   в README.md поруч із цим файлом. */

/* ===========================
   APP SHELL / BACKGROUND
   ===========================
   Фонове зображення чи відео (background.png / background.mp4)
   малює сам Theme Engine на весь екран -- див. manifest.json,
   поле "background". Тут можна хіба підправити колір-заглушку,
   поки фон завантажується: */
.app-shell {
  /* background: #0f1522; */
}

/* ===========================
   SIDEBAR
   =========================== */
.sidebar {
  /* background: rgba(20, 16, 30, 0.7); */
  /* border-right: 1px solid #7f6ae055; */
}
/* Пункт меню, який зараз відкритий (підсвічений) */
.nav-item.active {
  /* background: linear-gradient(100deg, #7f6ae044, #4a3a8f22); */
  /* box-shadow: inset 2px 0 var(--accent); */
}
/* Пункт меню під курсором */
.nav-item:hover {
  /* background: #1f2a3a; */
}

/* ===========================
   TOPBAR
   =========================== */
.topbar {
  /* background: #0d1117; */
  /* border-bottom: 1px solid #222c3a; */
}

/* ===========================
   CARDS / PANELS
   ===========================
   Один селектор одразу на всі типи карток в лаунчері (список
   інстансів, java, маркетплейс, акаунти, налаштування, черга
   завантажень і т.д.): */
.panel, .feature-card, .settings-card, .java-card, .managed-instance-card,
.project-card, .account-card, .download-builder, .download-queue,
.launch-debug-builder, .launch-debug-panel {
  /* background: #10151d; */
  /* border-color: #263041; */
  /* border-radius: 12px; */
}

/* ===========================
   HOME HERO
   ===========================
   Велика картка-банер на головній сторінці: */
.hero-card {
  /* background: linear-gradient(120deg, #17152a 0%, #161d35 55%, #102336 100%); */
}
.hero-copy h1 em {
  /* color: #7f6ae0; */
}

/* ===========================
   BUTTONS
   =========================== */
.primary-button {
  /* background: var(--accent); */
}
.primary-button:hover {
  /* box-shadow: 0 10px 28px rgba(127, 106, 224, 0.35); */
}
.secondary-button, button {
  /* border-radius: 7px; */
}

/* ===========================
   INPUTS / FORMS
   =========================== */
input, select, textarea {
  /* background: #10151d; */
  /* border-color: #263041; */
}

/* ===========================
   FONTS
   ===========================
   Готовий шрифт із fonts/ (див. fonts/README.txt) підключається тут
   через @font-face, потім використовується нижче: */
/* @font-face {
  font-family: "MyThemeFont";
  src: url("fonts/MyThemeFont.ttf");
} */
h1, h2, h3 {
  /* font-family: "MyThemeFont", "DM Mono", monospace; */
}

/* Порада: постав хоч один рядок без коментаря (як приклад нижче), щоб
   одразу побачити, що custom.css підключається і працює: */
.topbar { border-bottom: 1px solid var(--accent); }
"#;

// ── Hybrid CSS mode example files (pages/*.css) ───────────────────────────
//
// custom.css above is "Standard Mode" -- one file, whole launcher. These
// three are "Hybrid Mode": an OPTIONAL pages/ folder where sidebar.css and
// topbar.css always apply (global chrome, not tied to a route) and
// home.css applies only while the Home page is open. Load order is
// custom.css -> pages/sidebar.css -> pages/topbar.css -> pages/<page>.css,
// so a page file can override something custom.css set, and pages/ files
// are entirely optional -- a theme with no pages/ folder (or an old
// single-custom.css theme) behaves exactly as it always has.
const TEMPLATE_PAGE_SIDEBAR_CSS: &str = r#"/* ============================================================
   pages/sidebar.css -- застосовується ЗАВЖДИ, на всіх сторінках
   ============================================================
   На відміну від home.css/marketplace.css і т.д., sidebar.css і
   topbar.css не прив'язані до конкретної вкладки -- вони про
   глобальні елементи "рами" лаунчера, тому діють постійно. */
.sidebar {
  /* border-right: 2px solid var(--accent); */
}
"#;

const TEMPLATE_PAGE_TOPBAR_CSS: &str = r#"/* ============================================================
   pages/topbar.css -- застосовується ЗАВЖДИ, на всіх сторінках
   ============================================================ */
.topbar {
  /* background: #10151d; */
}
"#;

const TEMPLATE_PAGE_HOME_CSS: &str = r#"/* ============================================================
   pages/home.css -- застосовується ТІЛЬКИ на сторінці Home
   ============================================================
   Ім'я файлу = ключ вкладки (те саме, що в hiddenTabs/tabOrder):
   home.css, instances.css, marketplace.css, downloads.css,
   settings.css, accounts.css, logs.css, theme-maker.css,
   theme-editor.css, ai-helper.css -- додай будь-який з них за тим
   самим принципом, якого немає в архіві просто ігнорується. */

/* ---- Hero ---- */
.hero-card {
  /* background: linear-gradient(120deg, #1a1030 0%, #0f2a3a 100%); */
}

/* ---- News / feature cards на Home ---- */
.feature-card {
  /* border-color: var(--accent); */
}
"#;


pub fn theme_download_template_impl() -> Result<String, String> {
    let save_path = rfd::FileDialog::new()
        .set_title("Save theme template as")
        .set_file_name("theme-template.dftp")
        .add_filter("Dream Future Theme Pack", &["dftp"])
        .save_file()
        .ok_or_else(|| "Save cancelled.".to_string())?;

    let file = fs::File::create(&save_path).map_err(|error| error.to_string())?;
    let mut writer = zip::ZipWriter::new(file);
    let options = zip::write::SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated);

    writer.start_file("manifest.json", options).map_err(|error| error.to_string())?;
    writer.write_all(TEMPLATE_MANIFEST.as_bytes()).map_err(|error| error.to_string())?;

    writer.start_file("custom.css", options).map_err(|error| error.to_string())?;
    writer.write_all(TEMPLATE_CUSTOM_CSS.as_bytes()).map_err(|error| error.to_string())?;

    writer.start_file("README.md", options).map_err(|error| error.to_string())?;
    writer.write_all(TEMPLATE_README.as_bytes()).map_err(|error| error.to_string())?;

    writer.start_file("background.png", options).map_err(|error| error.to_string())?;
    writer.write_all(include_bytes!("../assets/theme-template/background.png")).map_err(|error| error.to_string())?;

    writer.start_file("background.mp4", options).map_err(|error| error.to_string())?;
    writer.write_all(include_bytes!("../assets/theme-template/background.mp4")).map_err(|error| error.to_string())?;

    writer.start_file("preview.png", options).map_err(|error| error.to_string())?;
    writer.write_all(include_bytes!("../assets/theme-template/preview.png")).map_err(|error| error.to_string())?;

    writer.start_file("pages/sidebar.css", options).map_err(|error| error.to_string())?;
    writer.write_all(TEMPLATE_PAGE_SIDEBAR_CSS.as_bytes()).map_err(|error| error.to_string())?;

    writer.start_file("pages/topbar.css", options).map_err(|error| error.to_string())?;
    writer.write_all(TEMPLATE_PAGE_TOPBAR_CSS.as_bytes()).map_err(|error| error.to_string())?;

    writer.start_file("pages/home.css", options).map_err(|error| error.to_string())?;
    writer.write_all(TEMPLATE_PAGE_HOME_CSS.as_bytes()).map_err(|error| error.to_string())?;

    writer.start_file("fonts/README.txt", options).map_err(|error| error.to_string())?;
    writer.write_all(TEMPLATE_FONTS_README.as_bytes()).map_err(|error| error.to_string())?;

    writer.start_file("icons/README.txt", options).map_err(|error| error.to_string())?;
    writer.write_all(TEMPLATE_ICONS_README.as_bytes()).map_err(|error| error.to_string())?;

    writer.finish().map_err(|error| error.to_string())?;
    Ok(save_path.to_string_lossy().to_string())
}

// ── AI chat context (Theme Maker's chat panel) ───────────────────────────
// Assembles the *text* parts of either the built-in template or the
// currently active installed theme, so the AI chat sees the real
// manifest/CSS shape instead of guessing it. Binary assets (background,
// fonts, icons) are intentionally left out -- irrelevant to CSS and not
// worth the token cost.

/// "Розробка" (development) mode: the same template shipped by
/// `theme_download_template`, so the AI is grounded in the exact
/// manifest schema and CSS conventions this build's engine expects.
pub fn ai_template_context() -> String {
    format!(
        "=== manifest.json ===\n{TEMPLATE_MANIFEST}\n\n\
         === custom.css (starter template -- mostly commented out, just shows the pattern/available hooks) ===\n{TEMPLATE_CUSTOM_CSS}\n\n\
         === pages/sidebar.css ===\n{TEMPLATE_PAGE_SIDEBAR_CSS}\n\n\
         === pages/topbar.css ===\n{TEMPLATE_PAGE_TOPBAR_CSS}\n\n\
         === pages/home.css ===\n{TEMPLATE_PAGE_HOME_CSS}\n\n\
         === launcher's REAL production stylesheet (every class actually used across every page, with current live values) -- this is what a custom.css override sits on top of; use it to find real selectors for parts of the UI the template above doesn't show (marketplace grid, instance cards, modals, forms, pills, progress bars, tabs, empty states, etc.) ===\n{LIVE_APP_CSS}\n"
    )
}

/// The id of the currently active installed theme, if any. Small public
/// helper so lib.rs (save_chat_message_as_css) can target
/// theme_write_page_css at the active theme without reaching into the
/// registry's internals itself.
pub fn active_theme_id(app: &AppHandle) -> Result<Option<String>, String> {
    Ok(read_registry(app)?.active_id)
}

/// "Оновлення теми" (update) mode: the user's currently active installed
/// theme's real manifest + custom.css + pages/*.css, so the AI edits what
/// actually exists instead of starting from the template.
pub fn ai_active_theme_context(app: &AppHandle) -> Result<String, String> {
    let registry = read_registry(app)?;
    let active_id = registry.active_id.clone()
        .ok_or_else(|| "No theme is currently active. Activate one in Theme Editor first, or switch the chat to \"Розробка\".".to_string())?;
    let record = registry.themes.iter().find(|record| record.id == active_id)
        .ok_or_else(|| "The active theme's record could not be found.".to_string())?;

    let manifest_json = serde_json::json!({
        "name": record.name, "author": record.author, "version": record.version,
        "engineVersion": ENGINE_VERSION, "mode": SUPPORTED_MODE,
        "background": record.background, "sidebarPosition": record.sidebar_position,
        "hiddenTabs": record.hidden_tabs, "tabOrder": record.tab_order,
    });
    let manifest_text = serde_json::to_string_pretty(&manifest_json).unwrap_or_default();

    let custom_css = theme_read_css_impl(app.clone(), active_id.clone())?.unwrap_or_default();
    let pages = theme_read_page_css_impl(app.clone(), active_id)?;

    let mut context = format!("=== manifest.json (current values) ===\n{manifest_text}\n\n=== custom.css (current) ===\n{}\n", if custom_css.trim().is_empty() { "(empty -- this theme has no custom.css yet)" } else { &custom_css });
    for (page_name, css) in pages {
        context.push_str(&format!("\n=== pages/{page_name}.css (current) ===\n{css}\n"));
    }
    context.push_str(&format!("\n=== launcher's REAL production stylesheet (every class actually used across every page, with current live values) -- use it to find real selectors for anything this theme's own CSS above doesn't already touch ===\n{LIVE_APP_CSS}\n"));
    Ok(context)
}


//
// Both of these are real, installable .dftp files -- but they don't show
// up in a fresh install's theme list (see seed_builtin_themes above).
// They're for theme authors: install one via Settings -> Theme Packs to
// see the technique in action, then throw it away.
#[tauri::command]
pub async fn theme_download_dev_example() -> Result<String, String> {
    tauri::async_runtime::spawn_blocking(move || {
        let save_path = rfd::FileDialog::new()
            .set_title("Save Developer Theme Example as")
            .set_file_name("developer-theme-example.dftp")
            .add_filter("Dream Future Theme Pack", &["dftp"])
            .save_file()
            .ok_or_else(|| "Save cancelled.".to_string())?;
        let file = fs::File::create(&save_path).map_err(|error| error.to_string())?;
        let mut writer = zip::ZipWriter::new(file);
        let options = zip::write::SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated);
        writer.start_file("manifest.json", options).map_err(|error| error.to_string())?;
        writer.write_all(br#"{
  "name": "Developer Theme Example",
  "author": "Dream Future Launcher",
  "version": "1.0.0",
  "engineVersion": "1.0",
  "mode": "engine"
}
"#).map_err(|error| error.to_string())?;
        writer.start_file("custom.css", options).map_err(|error| error.to_string())?;
        writer.write_all(DEV_EXAMPLE_CSS.as_bytes()).map_err(|error| error.to_string())?;
        writer.start_file("README.txt", options).map_err(|error| error.to_string())?;
        writer.write_all(b"NOT meant to look good. Install and activate this, then click through\nevery page of the launcher -- each loud color tells you exactly which\nCSS selector paints which region. Use it as a map, not a starting\npoint: copy the SELECTORS from custom.css into your own theme, not\nthe colors.\n").map_err(|error| error.to_string())?;
        writer.finish().map_err(|error| error.to_string())?;
        Ok(save_path.to_string_lossy().to_string())
    }).await.map_err(|error| error.to_string())?
}

#[tauri::command]
pub async fn theme_download_video_example() -> Result<String, String> {
    tauri::async_runtime::spawn_blocking(move || {
        let save_path = rfd::FileDialog::new()
            .set_title("Save Video Background Example as")
            .set_file_name("video-background-example.dftp")
            .add_filter("Dream Future Theme Pack", &["dftp"])
            .save_file()
            .ok_or_else(|| "Save cancelled.".to_string())?;
        let file = fs::File::create(&save_path).map_err(|error| error.to_string())?;
        let mut writer = zip::ZipWriter::new(file);
        let options = zip::write::SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated);
        writer.start_file("manifest.json", options).map_err(|error| error.to_string())?;
        writer.write_all(br#"{
  "name": "Video Background Example",
  "author": "Dream Future Launcher",
  "version": "1.0.0",
  "engineVersion": "1.0",
  "mode": "engine",
  "background": "background.mp4",
  "preview": "preview.png"
}
"#).map_err(|error| error.to_string())?;
        writer.start_file("custom.css", options).map_err(|error| error.to_string())?;
        writer.write_all(VIDEO_SHOWCASE_CSS.as_bytes()).map_err(|error| error.to_string())?;
        writer.start_file("background.mp4", options).map_err(|error| error.to_string())?;
        writer.write_all(include_bytes!("../assets/builtin-themes/video-showcase/background.mp4")).map_err(|error| error.to_string())?;
        writer.start_file("preview.png", options).map_err(|error| error.to_string())?;
        writer.write_all(include_bytes!("../assets/builtin-themes/video-showcase/preview.png")).map_err(|error| error.to_string())?;
        writer.finish().map_err(|error| error.to_string())?;
        Ok(save_path.to_string_lossy().to_string())
    }).await.map_err(|error| error.to_string())?
}


#[tauri::command]
pub async fn theme_install(app: AppHandle, archive_path: String) -> Result<ThemeInfo, String> {
    tauri::async_runtime::spawn_blocking(move || theme_install_impl(app, archive_path))
        .await
        .map_err(|error| error.to_string())?
}

pub fn theme_install_impl(app: AppHandle, archive_path: String) -> Result<ThemeInfo, String> {
    let source = PathBuf::from(&archive_path);
    if !source.is_file() { return Err("The .dftp file could not be found.".into()); }
    if source.extension().and_then(|e| e.to_str()).map(|e| e.eq_ignore_ascii_case("dftp")) != Some(true) {
        return Err("Only .dftp files can be installed.".into());
    }

    let manifest = read_manifest_from_zip(&source)?;
    validate_manifest(&manifest)?;

    let id = Uuid::new_v4().to_string();
    let folder = themes_root(&app)?.join(&id);
    fs::create_dir_all(&folder).map_err(|error| error.to_string())?;

    if let Err(error) = extract_dftp(&source, &folder) {
        let _ = fs::remove_dir_all(&folder);
        return Err(error);
    }

    // custom.css comes straight from a third-party archive — strip any
    // network-reaching url()/@import before it's ever trusted or read.
    let custom_css_path = folder.join("custom.css");
    if custom_css_path.is_file() {
        let raw = fs::read_to_string(&custom_css_path).map_err(|error| error.to_string())?;
        let cleaned = sanitize_custom_css(&raw);
        fs::write(&custom_css_path, cleaned).map_err(|error| error.to_string())?;
    }
    // "Hybrid CSS" mode: an optional pages/ folder with one .css per page
    // (home.css, marketplace.css, ...) plus global sidebar.css/topbar.css.
    // Auto-detected by presence, not a manifest field -- a theme with no
    // pages/ folder behaves exactly as before. Same sanitization as
    // custom.css applies to every file in there.
    sanitize_pages_dir(&folder);

    // Confirm referenced files actually exist post-extraction. background
    // is a hard requirement if declared; a missing preview just falls back
    // to no preview (frontend shows a placeholder) rather than failing the
    // whole install.
    let background = manifest.background.as_ref().filter(|f| folder.join(f).is_file());
    if manifest.background.is_some() && background.is_none() {
        let _ = fs::remove_dir_all(&folder);
        return Err("manifest.json references a background file that is not in the archive.".into());
    }
    let preview = manifest.preview.as_ref().filter(|f| folder.join(f).is_file()).cloned();
    let has_custom_css = folder.join("custom.css").is_file();
    let sidebar_position = manifest.sidebar_position.clone().map(|p| p.to_lowercase()).unwrap_or_else(default_sidebar_position);
    let hidden_tabs = manifest.hidden_tabs.clone().unwrap_or_default();
    let tab_order = manifest.tab_order.clone().unwrap_or_default();

    let record = ThemeRecord {
        id: id.clone(),
        name: manifest.name,
        author: manifest.author,
        version: manifest.version,
        background: background.cloned(),
        preview,
        has_custom_css,
        sidebar_position,
        hidden_tabs,
        tab_order,
    };

    let mut registry = read_registry(&app)?;
    registry.themes.push(record.clone());
    write_registry(&app, &registry)?;

    Ok(to_theme_info(&record, &folder, &registry.active_id))
}


#[tauri::command]
pub async fn theme_list(app: AppHandle) -> Result<Vec<ThemeInfo>, String> {
    tauri::async_runtime::spawn_blocking(move || theme_list_impl(app))
        .await
        .map_err(|error| error.to_string())?
}

pub fn theme_list_impl(app: AppHandle) -> Result<Vec<ThemeInfo>, String> {
    let registry = read_registry(&app)?;
    let root = themes_root(&app)?;
    Ok(registry.themes.iter().map(|record| to_theme_info(record, &root.join(&record.id), &registry.active_id)).collect())
}


#[tauri::command]
pub async fn theme_current(app: AppHandle) -> Result<Option<ThemeInfo>, String> {
    tauri::async_runtime::spawn_blocking(move || theme_current_impl(app))
        .await
        .map_err(|error| error.to_string())?
}

pub fn theme_current_impl(app: AppHandle) -> Result<Option<ThemeInfo>, String> {
    let registry = read_registry(&app)?;
    let root = themes_root(&app)?;
    Ok(registry.active_id.as_ref().and_then(|active_id| {
        registry.themes.iter().find(|record| &record.id == active_id).map(|record| to_theme_info(record, &root.join(&record.id), &registry.active_id))
    }))
}


#[tauri::command]
pub async fn theme_activate(app: AppHandle, theme_id: String) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || theme_activate_impl(app, theme_id))
        .await
        .map_err(|error| error.to_string())?
}

pub fn theme_activate_impl(app: AppHandle, theme_id: String) -> Result<(), String> {
    let mut registry = read_registry(&app)?;
    if !registry.themes.iter().any(|record| record.id == theme_id) {
        return Err("That theme is not installed.".into());
    }
    registry.active_id = Some(theme_id);
    write_registry(&app, &registry)
}


#[tauri::command]
pub async fn theme_deactivate(app: AppHandle) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || theme_deactivate_impl(app))
        .await
        .map_err(|error| error.to_string())?
}

pub fn theme_deactivate_impl(app: AppHandle) -> Result<(), String> {
    let mut registry = read_registry(&app)?;
    registry.active_id = None;
    write_registry(&app, &registry)
}


#[tauri::command]
pub async fn theme_remove(app: AppHandle, theme_id: String) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || theme_remove_impl(app, theme_id))
        .await
        .map_err(|error| error.to_string())?
}

pub fn theme_remove_impl(app: AppHandle, theme_id: String) -> Result<(), String> {
    let folder = themes_root(&app)?.join(&theme_id);
    if folder.exists() {
        fs::remove_dir_all(&folder).map_err(|error| error.to_string())?;
    }
    let mut registry = read_registry(&app)?;
    registry.themes.retain(|record| record.id != theme_id);
    if registry.active_id.as_deref() == Some(theme_id.as_str()) {
        registry.active_id = None;
    }
    write_registry(&app, &registry)
}


/// Reads the bundled custom.css content for an installed theme (if any),
/// so the frontend can inject it as a <style> tag. Returns None (not an
/// error) if the theme has no custom.css.
#[tauri::command]
pub async fn theme_read_css(app: AppHandle, theme_id: String) -> Result<Option<String>, String> {
    tauri::async_runtime::spawn_blocking(move || theme_read_css_impl(app, theme_id))
        .await
        .map_err(|error| error.to_string())?
}

pub fn theme_read_css_impl(app: AppHandle, theme_id: String) -> Result<Option<String>, String> {
    let path = themes_root(&app)?.join(&theme_id).join("custom.css");
    if !path.is_file() { return Ok(None); }
    fs::read_to_string(path).map(Some).map_err(|error| error.to_string())
}

/// Reads every *.css file under an installed theme's pages/ folder (the
/// "Hybrid CSS" mode -- see sanitize_pages_dir), keyed by filename
/// without the .css extension: "home.css" -> "home", "sidebar.css" ->
/// "sidebar", "topbar.css" -> "topbar". Returns an empty map (not an
/// error) if the theme has no pages/ folder -- old single-custom.css
/// themes keep working exactly as before.
#[tauri::command]
pub async fn theme_read_page_css(app: AppHandle, theme_id: String) -> Result<HashMap<String, String>, String> {
    tauri::async_runtime::spawn_blocking(move || theme_read_page_css_impl(app, theme_id))
        .await
        .map_err(|error| error.to_string())?
}

pub fn theme_read_page_css_impl(app: AppHandle, theme_id: String) -> Result<HashMap<String, String>, String> {
    let pages_dir = themes_root(&app)?.join(&theme_id).join("pages");
    let mut result = HashMap::new();
    let Ok(entries) = fs::read_dir(&pages_dir) else { return Ok(result); };
    for entry in entries.flatten() {
        let path = entry.path();
        if path.extension().and_then(|e| e.to_str()).map(|e| e.eq_ignore_ascii_case("css")) != Some(true) {
            continue;
        }
        let Some(stem) = path.file_stem().and_then(|s| s.to_str()) else { continue; };
        if let Ok(content) = fs::read_to_string(&path) {
            result.insert(stem.to_string(), content);
        }
    }
    Ok(result)
}

/// Writes (creates or overwrites) one pages/<page_key>.css file for an
/// already-installed theme -- the write-side counterpart to
/// theme_read_page_css. Used by the Theme Editor's per-page CSS blocks and
/// by save_chat_message_as_css (lib.rs) when the AI chat targets a specific
/// page instead of the single custom.css. Runs the same sanitize_custom_css
/// pass as install/packing so a hand-edited or AI-authored page CSS can't
/// smuggle in a network-reaching @import/url(). page_key goes through
/// sanitize_page_key first, same as theme_pack, so it can never escape the
/// theme's pages/ folder.
#[tauri::command]
pub async fn theme_write_page_css(app: AppHandle, theme_id: String, page_key: String, css: String) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || theme_write_page_css_impl(app, theme_id, page_key, css))
        .await
        .map_err(|error| error.to_string())?
}

pub fn theme_write_page_css_impl(app: AppHandle, theme_id: String, page_key: String, css: String) -> Result<(), String> {
    let safe_key = sanitize_page_key(&page_key).ok_or_else(|| format!("Invalid page key \"{page_key}\"."))?;
    let pages_dir = themes_root(&app)?.join(&theme_id).join("pages");
    fs::create_dir_all(&pages_dir).map_err(|error| error.to_string())?;
    let cleaned = sanitize_custom_css(&css);
    fs::write(pages_dir.join(format!("{safe_key}.css")), cleaned).map_err(|error| error.to_string())
}


/// Theme Editor (as opposed to Theme Maker): edits LAYOUT settings of an
/// ALREADY INSTALLED theme in place — sidebar position, hidden tabs, tab
/// order. Does not touch background/preview/fonts/custom.css/name/author/
/// version; those stay creation-time-only via theme_pack. Updates both the
/// registry (source of truth for the running app) and the on-disk
/// manifest.json (so re-sharing the installed theme later keeps these
/// settings).
#[tauri::command]
pub async fn theme_update_layout(app: AppHandle, theme_id: String, sidebar_position: String, hidden_tabs: Vec<String>, tab_order: Vec<String>) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || theme_update_layout_impl(app, theme_id, sidebar_position, hidden_tabs, tab_order))
        .await
        .map_err(|error| error.to_string())?
}

pub fn theme_update_layout_impl(app: AppHandle, theme_id: String, sidebar_position: String, hidden_tabs: Vec<String>, tab_order: Vec<String>) -> Result<(), String> {
    let position = sidebar_position.to_lowercase();
    if !SIDEBAR_POSITIONS.contains(&position.as_str()) {
        return Err(format!("Invalid sidebar position \"{sidebar_position}\" — must be one of: left, right, top, bottom."));
    }
    if let Some(error) = locked_tab_error(&hidden_tabs) {
        return Err(error);
    }

    let mut registry = read_registry(&app)?;
    {
        let record = registry.themes.iter_mut().find(|record| record.id == theme_id).ok_or_else(|| "That theme is not installed.".to_string())?;
        record.sidebar_position = position.clone();
        record.hidden_tabs = hidden_tabs.clone();
        record.tab_order = tab_order.clone();
    }
    write_registry(&app, &registry)?;

    // Best-effort sync back into the on-disk manifest.json too. Not fatal
    // if this part fails — the registry (already saved above) is what the
    // running app actually reads from.
    let manifest_path = themes_root(&app)?.join(&theme_id).join("manifest.json");
    if let Ok(text) = fs::read_to_string(&manifest_path) {
        if let Ok(mut value) = serde_json::from_str::<serde_json::Value>(&text) {
            if let Some(object) = value.as_object_mut() {
                object.insert("sidebarPosition".into(), serde_json::json!(position));
                object.insert("hiddenTabs".into(), serde_json::json!(hidden_tabs));
                object.insert("tabOrder".into(), serde_json::json!(tab_order));
            }
            if let Ok(pretty) = serde_json::to_string_pretty(&value) {
                let _ = fs::write(&manifest_path, pretty);
            }
        }
    }
    Ok(())
}


#[tauri::command]
pub async fn browse_custom_css_file() -> Result<Option<String>, String> {
    tauri::async_runtime::spawn_blocking(move || browse_custom_css_file_impl())
        .await
        .map_err(|error| error.to_string())?
}

pub fn browse_custom_css_file_impl() -> Result<Option<String>, String> {
    Ok(rfd::FileDialog::new()
        .set_title("Select a custom .css file")
        .add_filter("CSS", &["css"])
        .pick_file()
        .map(|path| path.to_string_lossy().into_owned()))
}


#[tauri::command]
pub async fn browse_dftp_file() -> Result<Option<String>, String> {
    tauri::async_runtime::spawn_blocking(move || browse_dftp_file_impl())
        .await
        .map_err(|error| error.to_string())?
}

pub fn browse_dftp_file_impl() -> Result<Option<String>, String> {
    Ok(rfd::FileDialog::new()
        .set_title("Select a .dftp theme file")
        .add_filter("Dream Future Theme Pack", &["dftp"])
        .pick_file()
        .map(|path| path.to_string_lossy().into_owned()))
}


// ── Theme Maker (STEP H) — pack a brand-new .dftp from user-chosen files ──

#[tauri::command]
pub async fn browse_theme_asset() -> Result<Option<String>, String> {
    tauri::async_runtime::spawn_blocking(move || browse_theme_asset_impl())
        .await
        .map_err(|error| error.to_string())?
}

pub fn browse_theme_asset_impl() -> Result<Option<String>, String> {
    Ok(rfd::FileDialog::new()
        .set_title("Select an image or video file")
        .add_filter("Images/Video", &["png", "jpg", "jpeg", "webp", "gif", "mp4", "webm"])
        .pick_file()
        .map(|path| path.to_string_lossy().into_owned()))
}


fn slugify(name: &str) -> String {
    let slug: String = name.trim().to_lowercase().chars()
        .map(|c| if c.is_ascii_alphanumeric() { c } else { '-' })
        .collect();
    if slug.trim_matches('-').is_empty() { "theme".to_string() } else { slug }
}

#[tauri::command]
pub async fn browse_theme_fonts() -> Result<Vec<String>, String> {
    tauri::async_runtime::spawn_blocking(move || browse_theme_fonts_impl())
        .await
        .map_err(|error| error.to_string())?
}

pub fn browse_theme_fonts_impl() -> Result<Vec<String>, String> {
    Ok(rfd::FileDialog::new()
        .set_title("Select font files")
        .add_filter("Fonts", &["ttf", "otf"])
        .pick_files()
        .unwrap_or_default()
        .into_iter()
        .map(|path| path.to_string_lossy().into_owned())
        .collect())
}


/// Packs a new .dftp from user-supplied fields + already-chosen source
/// files (picked separately via browse_theme_asset), then opens a native
/// "save as" dialog and writes the archive there. Returns the final saved
/// path, or an error if the user cancels the save dialog or something in
/// the zip-writing step fails.
///
/// ⚠️ NOT verified by an actual local build: the `zip` crate's write-side
/// API (SimpleFileOptions) had naming changes across its 2.x minor
/// versions. If this fails to compile with an error pointing at
/// `zip::write::SimpleFileOptions`, check the installed `zip` version's
/// docs for the current write-options type name (it may need to be
/// `zip::write::FileOptions::default()` instead, or similar) and swap it
/// in — the rest of the function does not depend on which name is right.
///
/// Fonts are dropped into a fonts/ folder in the archive as-is (original
/// filenames kept). This only PACKS them — actually applying a custom font
/// to the launcher UI when a theme is active is a separate, not-yet-built
/// step (reading fonts/*.ttf|*.otf back out and injecting @font-face rules
/// at theme-activation time).
#[tauri::command]
pub async fn theme_pack(name: String, author: String, version: String, background_path: Option<String>, preview_path: Option<String>, font_paths: Vec<String>, custom_css_path: Option<String>, sidebar_position: String, hidden_tabs: Vec<String>, tab_order: Vec<String>, page_css_paths: Option<HashMap<String, String>>) -> Result<String, String> {
    tauri::async_runtime::spawn_blocking(move || theme_pack_impl(name, author, version, background_path, preview_path, font_paths, custom_css_path, sidebar_position, hidden_tabs, tab_order, page_css_paths.unwrap_or_default()))
        .await
        .map_err(|error| error.to_string())?
}

pub fn theme_pack_impl(name: String, author: String, version: String, background_path: Option<String>, preview_path: Option<String>, font_paths: Vec<String>, custom_css_path: Option<String>, sidebar_position: String, hidden_tabs: Vec<String>, tab_order: Vec<String>, page_css_paths: HashMap<String, String>) -> Result<String, String> {
    if name.trim().is_empty() { return Err("Theme name is required.".into()); }
    if author.trim().is_empty() { return Err("Author is required.".into()); }
    if version.trim().is_empty() { return Err("Version is required.".into()); }
    let position = sidebar_position.to_lowercase();
    if !SIDEBAR_POSITIONS.contains(&position.as_str()) {
        return Err(format!("Invalid sidebar position \"{sidebar_position}\" — must be one of: left, right, top, bottom."));
    }
    if let Some(error) = locked_tab_error(&hidden_tabs) {
        return Err(error);
    }

    let save_path = rfd::FileDialog::new()
        .set_title("Save theme pack as")
        .set_file_name(&format!("{}.dftp", slugify(&name)))
        .add_filter("Dream Future Theme Pack", &["dftp"])
        .save_file()
        .ok_or_else(|| "Save cancelled.".to_string())?;

    let background_entry = match &background_path {
        Some(path) => {
            let ext = Path::new(path).extension().and_then(|e| e.to_str()).unwrap_or("").to_lowercase();
            if !SUPPORTED_BACKGROUND_EXTENSIONS.contains(&ext.as_str()) {
                return Err(format!("Unsupported background file type: \".{ext}\""));
            }
            Some(format!("background.{ext}"))
        }
        None => None,
    };
    let preview_entry = preview_path.as_ref().map(|path| {
        let ext = Path::new(path).extension().and_then(|e| e.to_str()).unwrap_or("png").to_lowercase();
        format!("preview.{ext}")
    });

    // Validate all fonts up front, before opening/writing anything to the
    // archive, so a bad font doesn't leave a half-written .dftp behind.
    for font_path in &font_paths {
        let ext = Path::new(font_path).extension().and_then(|e| e.to_str()).unwrap_or("").to_lowercase();
        if ext != "ttf" && ext != "otf" {
            return Err(format!("Unsupported font file type: \".{ext}\" (only .ttf/.otf are supported)."));
        }
    }

    // Same up-front validation for Hybrid Mode's per-page CSS files: reject
    // an unexpected key or an unreadable path before anything is written,
    // rather than leaving a half-packed .dftp behind. Keys are the fixed
    // set Theme Maker's own UI offers (sidebar/topbar/page nav keys), never
    // arbitrary user text, but this is still a Tauri command boundary --
    // sanitize_page_key keeps a key like "../../evil" from ever becoming a
    // zip entry path.
    for (page_key, css_path) in &page_css_paths {
        if sanitize_page_key(page_key).is_none() {
            return Err(format!("Invalid page key \"{page_key}\"."));
        }
        if !Path::new(css_path).is_file() {
            return Err(format!("Page CSS file not found: {css_path}"));
        }
    }

    let manifest = ThemeManifest {
        name,
        author,
        version,
        engine_version: ENGINE_VERSION.to_string(),
        mode: SUPPORTED_MODE.to_string(),
        background: background_entry.clone(),
        preview: preview_entry.clone(),
        sidebar_position: Some(position),
        hidden_tabs: Some(hidden_tabs),
        tab_order: Some(tab_order),
    };
    let manifest_json = serde_json::to_string_pretty(&manifest).map_err(|error| error.to_string())?;

    let file = fs::File::create(&save_path).map_err(|error| error.to_string())?;
    let mut writer = zip::ZipWriter::new(file);
    let options = zip::write::SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated);

    writer.start_file("manifest.json", options).map_err(|error| error.to_string())?;
    writer.write_all(manifest_json.as_bytes()).map_err(|error| error.to_string())?;

    if let (Some(source), Some(entry)) = (&background_path, &background_entry) {
        let bytes = fs::read(source).map_err(|error| error.to_string())?;
        writer.start_file(entry, options).map_err(|error| error.to_string())?;
        writer.write_all(&bytes).map_err(|error| error.to_string())?;
    }
    if let (Some(source), Some(entry)) = (&preview_path, &preview_entry) {
        let bytes = fs::read(source).map_err(|error| error.to_string())?;
        writer.start_file(entry, options).map_err(|error| error.to_string())?;
        writer.write_all(&bytes).map_err(|error| error.to_string())?;
    }
    for font_path in &font_paths {
        let filename = Path::new(font_path).file_name()
            .ok_or_else(|| format!("Invalid font file path: {font_path}"))?
            .to_string_lossy().to_string();
        let bytes = fs::read(font_path).map_err(|error| error.to_string())?;
        writer.start_file(format!("fonts/{filename}"), options).map_err(|error| error.to_string())?;
        writer.write_all(&bytes).map_err(|error| error.to_string())?;
    }
    if let Some(css_path) = &custom_css_path {
        let bytes = fs::read(css_path).map_err(|error| error.to_string())?;
        writer.start_file("custom.css", options).map_err(|error| error.to_string())?;
        writer.write_all(&bytes).map_err(|error| error.to_string())?;
    }
    // Hybrid Mode: one pages/<key>.css entry per page the user attached a
    // file for. Presence of a pages/ folder at all is what the theme engine
    // already treats as "this theme also has per-page overrides" -- no
    // separate manifest flag needed (see theme_read_page_css_impl, which
    // just reads whatever pages/*.css files happen to exist).
    for (page_key, css_path) in &page_css_paths {
        let Some(safe_key) = sanitize_page_key(page_key) else { continue };
        let bytes = fs::read(css_path).map_err(|error| error.to_string())?;
        writer.start_file(format!("pages/{safe_key}.css"), options).map_err(|error| error.to_string())?;
        writer.write_all(&bytes).map_err(|error| error.to_string())?;
    }

    writer.finish().map_err(|error| error.to_string())?;
    Ok(save_path.to_string_lossy().to_string())
}

/// Only lowercase ascii letters, digits, and hyphens -- matches every real
/// page key (nav route keys like "instances"/"ai-helper", plus the two
/// always-on "sidebar"/"topbar" pseudo-pages). Rejects anything else
/// (empty, path separators, "..", etc.) instead of letting it become a zip
/// entry path.
fn sanitize_page_key(key: &str) -> Option<String> {
    let trimmed = key.trim().to_lowercase();
    if trimmed.is_empty() || trimmed.len() > 64 { return None; }
    if !trimmed.chars().all(|c| c.is_ascii_alphanumeric() || c == '-') { return None; }
    Some(trimmed)
}

