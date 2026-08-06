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