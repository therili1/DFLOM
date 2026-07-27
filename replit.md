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

## User preferences

- Using Replit to browse and edit source code only (no run workflow needed).
