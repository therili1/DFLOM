# SkyLight Launcher (DFLOM)

A WinUI3 / .NET 8 Minecraft launcher for Windows, built with C#.

## Stack
- **Framework:** WinUI3 (.NET 8, `net8.0-windows10.0.19041.0`)
- **UI:** XAML + WinUI3, CommunityToolkit.Mvvm (MVVM pattern)
- **Minecraft:** CmlLib.Core (game download/launch), CmlLib.Core.Auth.Microsoft (auth)
- **Mod loaders:** Fabric, Quilt, Forge via CmlLib installers
- **Marketplace:** Modrinth API v2 (mods, resource packs, shaders)
- **DI:** Microsoft.Extensions.Hosting (singleton services, loaded before MainWindow)
- **Persistence:** JSON files in `%AppData%/.lrs_launcher/`

## Important notes

### Cannot run on Replit
This project targets Windows only. It requires WinUI3 and the Windows App SDK, which are not available on Linux. To build and run, use Visual Studio 2022 on Windows and open `Launcher.sln`.

### Architecture rules
- All services are registered as **Singleton** in `App.xaml.cs` — do NOT change to Transient or page state will reset on tab switch.
- Services must be fully loaded (via `LoadAsync()`) in `OnLaunched` **before** `MainWindow` is created, or UI will flash default values on startup.
- ViewModels must be assigned **before** `InitializeComponent()` in pages that have `ComboBoxItem IsSelected="True"` in XAML, or `SelectionChanged` fires before ViewModel exists → `NullReferenceException`.
- `GameDirectory` must always be set on every `MinecraftInstance` — empty directory means all instances share one folder and stomp each other's loaders/mods.

### Loader version ID resolution
`FabricInstaller.Install()` / `QuiltInstaller.Install()` return only the loader version number (e.g. `"0.19.3"`), not the full folder ID (`"fabric-loader-0.19.3-1.21.1"`). `MinecraftService.ResolveInstalledLoaderVersionId` handles this: it checks the exact expected ID first, then falls back to prefix+mcVersion search (sorted by folder name descending, NOT creation time), then bare folder with both `.jar` + `.json`.

### Persistence paths (all JSON, all in `%AppData%/.lrs_launcher/`)
- `instances.json` — instance list
- `theme.json` — theme settings
- `navigation.json` — nav settings
- `animations.json` — animation settings

## User preferences
- Language of code comments and UI strings: Ukrainian
- Bug fixes only — do not restructure or migrate the project
