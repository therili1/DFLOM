# SkyLight Launcher (DFLOM)

A WinUI 3 / C# Windows desktop Minecraft launcher with a full theme engine, navigation customization, and mod/modpack management.

## Stack

- **Language:** C# (.NET, WinUI 3)
- **Pattern:** MVVM (Models / ViewModels / Views)
- **UI framework:** WinUI 3 (Windows App SDK)
- **Solution file:** `Launcher.sln` / `Launcher.csproj`

## Project layout

```
Models/          — Data models (ModItem, SavedAccount, ThemeSettings, WorldItem, etc.)
Services/        — Business logic & interfaces (Auth, Download, Theme, Navigation, Instances, etc.)
ViewModels/      — MVVM view-models (Home, Instances, Marketplace, ThemeEditor, etc.)
Views/           — XAML pages and dialogs
Converters/      — WPF/WinUI value converters
App.xaml(.cs)    — App entry point
MainWindow.xaml(.cs) — Shell / navigation host
```

## Important notes

- **Windows-only** — WinUI 3 targets Windows exclusively; this project cannot be built or run on Replit's Linux environment.
- To build and run locally, open `Launcher.sln` in Visual Studio 2022 with the Windows App SDK workload installed.
- User data (themes, navigation settings, animations) is stored in `%AppData%/.lrs_launcher/`.
- The Discord community hub link in `MainWindow.xaml.cs` is currently a placeholder (`discord.gg/`) — replace it with the real invite URL before shipping.

## Release EXE crash fix (2025-07-27)

**Symptom**: Published Release EXE exited immediately with no window, no exception, no dialog.

**Root causes and fixes:**

### 1. `async void OnLaunched` — no exception handling (`App.xaml.cs`)
In Release, any unhandled exception in an `async void` method silently terminates the process — no dialog, no stack trace. The VS debugger hides this in Debug mode.  
**Fix:** Wrapped every startup stage in its own `try/catch`. Exceptions are appended to `%AppData%\.lrs_launcher\startup_crash.log` so the developer can always see what actually threw. Fatal stages (`Create MainWindow`, `Activate`) re-throw; non-fatal ones (service load) fall back to defaults and continue.

### 2. `SelectFirstAvailableItem()` called in `MainWindow` constructor (`MainWindow.xaml.cs`)
The constructor called `SelectFirstAvailableItem()` → `ContentFrame.Navigate(HomeView)` → `HomeView()` → `App.GetService<HomeViewModel>()` → `App.GetService<IMinecraftService>()` → `MinecraftService()` → `new MinecraftLauncher(path)`. This entire chain ran *inside* the constructor, before `Activate()` was called. Any exception in that chain propagated out of `async void OnLaunched` with no handler.  
**Fix:** Removed the `SelectFirstAvailableItem()` call from the constructor; subscribed to `this.Loaded` instead. The `Loaded` event fires after the window is visible (`Activate()` has been called), so page/ViewModel construction now happens in a safe state.

### 3. `DispatcherQueue.GetForCurrentThread()` without null-guards (`MarketplaceViewModel`, `MonitoringViewModel`, `DownloadCenterViewModel`)
`GetForCurrentThread()` returns `null` if ever called from a non-UI thread. All three ViewModels stored the result without null-checking it, then called `_dispatcherQueue.TryEnqueue(...)` from background event handlers — a NullReferenceException waiting to happen.  
**Fix:** Added `?.` null-conditional on every `TryEnqueue` call site. The `GetForCurrentThread()` call itself stays in the constructor (correct, since these Singletons are always first resolved from page constructors on the UI thread), but now it is safe to run even if somehow resolved off-thread.

## User preferences

- Using Replit to browse and edit source code only (no run workflow needed).
