Dream Future Launcher

Dream Future Launcher is a modern, fast and fully customizable Minecraft launcher built with Rust (Tauri). It is designed for players who want complete control over their Minecraft experience while keeping the launcher lightweight, secure and easy to use.

Unlike traditional launchers, Dream Future Launcher focuses on flexibility, performance and customization. Every part of the interface can be personalized using the built-in Theme Engine, allowing users to completely transform the appearance of the launcher without affecting its functionality.

⚡ Key Features
🎨 Fully Customizable Interface

Dream Future Launcher includes one of its most important features — a powerful Theme Engine.

Users can:

Install custom themes in DFTP format
Browse and download themes from the integrated Theme Marketplace
Create their own themes using the built-in Theme Maker
Customize backgrounds, colors, fonts, animations and interface elements
Share themes with the community

The launcher is designed so every user can build their own unique experience.

🔐 Secure Microsoft Authentication

Dream Future Launcher supports official Microsoft account authentication for Minecraft Java Edition.

The launcher:

Uses official Microsoft authentication
Never bypasses licensing or security checks
Requires a legitimate Minecraft account
Stores tokens securely
Supports multiple accounts
📦 Modrinth Integration

Manage mods directly inside the launcher.

Current support includes:

Mod browsing
Search
Version compatibility
Automatic downloads
Dependency handling
Easy installation
🗂 Instance Management

Create and manage multiple Minecraft instances.

Each instance has its own:

Minecraft version
Mods
Resource packs
Java version
Settings
Saves

Perfect for switching between different modpacks or gameplay styles.

☕ Java Runtime Management

Dream Future Launcher automatically detects installed Java versions and can download compatible runtimes when required.

Features include:

Java detection
Runtime validation
Version compatibility
Automatic selection
📥 Download Manager

The integrated download manager handles:

Minecraft versions
Assets
Libraries
Java runtimes
Mods

Downloads are verified to ensure file integrity.

📊 Performance Monitoring

Monitor launcher activity in real time.

Includes:

Memory usage
CPU usage
Download progress
Launcher logs
🛠 Built with Rust

Dream Future Launcher is powered by Rust and Tauri, making it:

Lightweight
Fast
Memory efficient
Secure
Native

Unlike Electron-based launchers, Dream Future Launcher starts quickly and uses significantly fewer system resources.

Roadmap

Dream Future Launcher is under active development.

Planned Features
✅ CurseForge Integration
✅ Minecraft Bedrock for PC Support
✅ Cloud Synchronization
✅ Automatic Updates
✅ Plugin System
✅ Community Marketplace Improvements
✅ More Theme Engine Features
✅ Better Performance Monitoring
✅ Cross-platform Support (future)
Open Source

Dream Future Launcher is an open-source project.

GitHub:
https://github.com/therili1/DFLOM

Contributions, bug reports and suggestions are always welcome.

Current Version

Public Beta v0.7

Dream Future Launcher is actively developed and receives frequent updates.

Vision

Dream Future Launcher aims to become one of the most customizable Minecraft launchers available.

Our goal is simple:

Give every Minecraft player complete freedom to customize not only the game — but also the launcher itself.
# Dream Future Launcher

Cross-platform desktop launcher foundation built with Tauri 2, React,
TypeScript, Vite, and Zustand.

## Requirements

- Node.js 20 or newer
- Rust toolchain with Cargo
- Tauri 2 prerequisites for your operating system

## Development

```bash
npm install
npm run tauri dev
```

On Windows, double-click `setup-and-run.bat`.

## Production build

```bash
npm install
npm run tauri build
```

On Windows, use `build-windows.bat`.

The project intentionally does not use Electron, Photino, Replit APIs, or
Replit-specific package registries.
