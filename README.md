# Relay — AI Vision & Desktop Intelligence for Windows

<div align="center">

**Relay is a high-performance native Windows desktop AI assistant that understands whatever you are looking at on your screen.**

Think of it as **Google Lens for the entire desktop**, built with **.NET 10 (WPF + Win32)**, **Gemini Multimodal Vision**, and **Offline Windows OCR**.

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows 11](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D4?logo=windows)](https://microsoft.com)
[![Gemini Multimodal](https://img.shields.io/badge/AI-Google%20Gemini%20Flash--Lite-4285F4?logo=google)](https://ai.google.dev/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

</div>

---

## 🌟 Key Highlights & Architecture

* **Global System Hotkey (`Ctrl + Space`)**: Instantly freeze and select any rectangular region on any display across heterogeneous multi-monitor setups.
* **Pixel-Perfect GDI+ Screen Capture (< 10ms)**: Sub-millisecond desktop snapshotting with Per-Monitor V2 DPI scaling normalization.
* **Instant Offline OCR**: Built-in Windows 10/11 `Windows.Media.Ocr` engine extracts on-screen text in under 20ms at zero API cost.
* **Operating System & Window Context**: Automatically inspects the active application (`Visual Studio`, `Google Chrome`, `VS Code`, `Spotify`, etc.) and window title to enrich AI understanding.
* **Intelligent Intent Engine**: Automatically classifies screen content into actions:
  * `DEBUG` — Diagnoses compiler exceptions, stack traces, and provides 1-click code fixes.
  * `SHOP` — Identifies products, exact models, brands, and search prices.
  * `TRANSLATE` — Detects foreign on-screen text and provides translations.
  * `EXPLAIN` — Breaks down scientific diagrams, math formulas, and complex UI.
  * `EXTRACT` — Formats tabular data, markdown, and code snippets ready for clipboard copy.
* **Streaming Glassmorphic Floating Panel**: Token-by-token streaming markdown with interactive ChatGPT-style code blocks, 1-click copy buttons, always-on-top pinning, and continuous multi-turn follow-up chat.
* **Zero-Leakage Privacy Architecture**:
  * API keys encrypted at rest via **Windows DPAPI** (`CurrentUser` scope).
  * No screenshots persisted to disk by default (in-memory analysis).
  * Application Blacklist (e.g. 1Password, Bitwarden, KeePass) prevents accidental capture of sensitive windows.
* **Local SQLite History**: Searchable query and answer history stored locally in `%LocalAppData%\Relay\relay.db`.

---

## 🏗 Solution Structure

```text
Relay/
├── src/
│   ├── Relay.Core/                   # Domain models, enums (IntentType), and contracts
│   │   ├── Interfaces/               # IAiProvider, IScreenCaptureService, IHotkeyService, etc.
│   │   └── Models/                   # CaptureRegion, ScreenContext, AiAnalysisRequest, etc.
│   │
│   ├── Relay.Infrastructure/         # Concrete implementations & Win32 Interop
│   │   ├── Ai/                       # Gemini Flash-Lite & Ollama Multimodal SSE streaming clients
│   │   ├── ScreenCapture/            # High-performance GDI+ BitBlt & DPI conversion
│   │   ├── Hotkeys/                  # Win32 RegisterHotKey with message hook
│   │   ├── WindowContext/            # GetForegroundWindow & process metadata
│   │   ├── Ocr/                      # Native Windows.Media.Ocr wrapper
│   │   ├── Security/                 # DPAPI encrypted credential vault
│   │   ├── Search/                   # Web search integration service
│   │   └── Data/                     # EF Core SQLite DbContext & repositories
│   │
│   ├── Relay.UI/                     # WPF Windows 11 Desktop Application
│   │   ├── Controls/                 # Streaming Markdown viewer, badges, cards
│   │   ├── Styles/                   # Colors, typography, Fluent dark theme
│   │   ├── ViewModels/               # MVVM ViewModels (CommunityToolkit.Mvvm)
│   │   └── Views/                    # Overlay, Floating Result, Settings, History windows
│   │
│   └── Relay.Tests/                  # Unit & integration test suite (xUnit, FluentAssertions, Moq)
└── Relay.slnx
```

---

## 🚀 Getting Started

### Prerequisites
* Windows 10 (Version 19041+) or Windows 11
* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
* A [Google Gemini API Key](https://aistudio.google.com/) or a local Ollama instance

### Build & Run from Source
1. Run with `run.cmd` or build from CLI:
   ```bash
   dotnet build
   dotnet run --project src/Relay.UI/Relay.UI.csproj
   ```
2. Enter your Gemini API key in the **Settings** dialog or choose Local AI (Ollama).
3. Press `Ctrl + Space` anywhere in Windows to start analyzing your screen!

---

## 📦 Windows Installer & Background Startup (.exe)

Relay can be built as a standalone Windows installer (`RelaySetup.exe`) that sets it up as a lightweight startup background app:

### Building the Installer
Run the one-click build script:
```cmd
build-installer.cmd
```
*(Or in PowerShell: `.\build-installer.ps1`)*

This publishes a self-contained, optimized Ahead-of-Time ReadyToRun binary and compiles `dist/RelaySetup.exe`.

### Key Installer & Background Features
* **Zero-Elevation Per-User Install**: Installs directly into `%LocalAppData%\Programs\Relay` without requiring Administrator/UAC elevation.
* **Seamless PC Startup**: Automatically starts with Windows (`--minimized`) in the background system tray.
* **Minimal Resource Footprint**: Idle memory is trimmed to **~10–15 MB RAM** with **0% background CPU** utilization when sitting in the system tray.
* **Single-Instance Enforcement**: Re-opening Relay focuses the existing active instance without duplicate processes or hotkey conflicts.
* **Clean Uninstaller**: Registered in Windows *Installed Apps* / *Add or Remove Programs*.

---

## 🧪 Running Automated Tests

```bash
dotnet test
```

---

## 🛡 Privacy & Security

* Relay only captures the specific pixel region you drag-select.
* Visual data is processed in memory during the active session.
* API credentials are encrypted with your Windows user credentials via DPAPI.
* You can configure blacklisted processes in Settings to prevent Relay from activating when sensitive applications are focused.

---

## 📄 License
MIT License. Open source and built for modern Windows productivity.
