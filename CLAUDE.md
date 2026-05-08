# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build
dotnet build src/xDocHunter/xDocHunter.csproj

# Build and launch — kills any running xDocHunter.exe first (build.bat)
build.bat

# Run
dotnet run --project src/xDocHunter/xDocHunter.csproj

# Publish self-contained single-file exe
dotnet publish src/xDocHunter/xDocHunter.csproj -c Release

# Regenerate app-icon.ico from app-icon.png (run from repo root)
powershell -File tools/make-icon.ps1
```

## Versioning

Version is declared in two places — keep them in sync manually:
- `version.json` — `"version"` field
- `src/xDocHunter/xDocHunter.csproj` — `<Version>`, `<AssemblyVersion>`, `<FileVersion>`

## Run tests
```bash
dotnet test tests/xDocHunter.Tests/xDocHunter.Tests.csproj
```

Tests live in `tests/xDocHunter.Tests/`. All file I/O in tests is confined to a per-test GUID temp directory via `TestSandbox` — no writes go to `%LocalAppData%\xDocHunter\` or user files. No linter is configured.

| Test file | Coverage |
|---|---|
| `TestSandbox.cs` | Per-test GUID temp directory helper |
| `DatabaseTests.cs` | SQLite schema, search, import/export |
| `FileScannerTests.cs` | BFS enumeration, extension filter behavior |
| `TextExtractorTests.cs` | PDF, DOCX, XLSX, plain-text extraction |
| `RoundTripTests.cs` | Scan → save → import round-trip |
| `ModelsTests.cs` | FileEntry, ScanOptions, ExtensionFilter logic |

## Architecture

xDocHunter is a WPF desktop app (.NET 8, win-x64) for indexing and searching files **by name OR by content**. Uses MVVM with CommunityToolkit.Mvvm source generators.

### Search modes

The app has two search modes, picked per scan in the **Scan Options** dialog:

- **Filename mode** — fast scan, indexes file names and paths only. Search uses `LIKE` queries on `name` + `full_path`. No content extraction.
- **Content mode** — slower scan, extracts text from PDFs, Office docs, code, and 40+ text formats via `TextExtractor`. Search uses FTS5 MATCH on the `file_content` column (and optionally `name`).

The current mode is shown as a small badge in the footer (`[FILENAME]` / `[CONTENT]`) and stored in `MainViewModel.CurrentSearchMode`. The default mode is configurable in **Settings → Search → Default search mode**.

### Core data flow

1. **FileScanner** (`Services/FileScanner.cs`) enumerates a directory tree via BFS, yielding `FileEntry` records as an `IAsyncEnumerable`. Receives a `ScanFilterOptions` (with `Mode`); only calls `TextExtractor` when `Mode == SearchMode.Content`.
2. **Database** (`Services/Database.cs`, SQLite via Microsoft.Data.Sqlite) stores entries in batches of 1000. Uses WAL mode + FTS5 virtual table on `name` + `file_content`. The `Search()` method picks LIKE vs. FTS5 based on the `mode` parameter.
3. **MainViewModel** orchestrates scanning, importing, searching, and folder-tree building. Single ViewModel — both `WelcomeView` and `MainWindow` bind to it.
4. Indexes can be saved as `.nfindex` (SQLite) or `.nfindex.json`. The current `SearchMode` is persisted in the export's `metadata.search_mode` row, and `file_content` is preserved when present.

### .nfindex import compatibility

`Database.ImportNfindexAsync` auto-detects the schema source and returns `(count, SearchMode)`:

| Source | Detection | Imported as |
|---|---|---|
| xDocHunter | `metadata.search_mode` exists | Mode read from metadata; content imported if `file_content` column exists |
| Legacy internal `.db` | `files.full_path` + `files.file_content` columns | Content mode |
| Legacy `.nfindex` | `files.path` column, no `search_mode` metadata | Filename mode |

### Key files

- `App.xaml.cs` — startup entry point; calls `ThemeManager.Initialize()` on launch
- `Models/FileEntry.cs` — core data model; also defines `ScanProgress`, `ImportProgress`, and `ExtensionFilter` records
- `Models/FolderNode.cs` — tree node model for the folder-tree sidebar; built from scan results in MainViewModel
- `Models/SearchMode.cs` — `enum SearchMode { Filename, Content }`
- `Converters.cs` — WPF value converters (visibility, size formatting, mode display) used in XAML bindings
- `Models/ScanOptions.cs` — `ScanFilterOptions` (has `Mode` + `AllowedExtensions`); `FileTypePreset.Defaults()` returns 12 merged presets (PDFs, Office, Text, Code, Web, Config, Scripts, Images, Videos, Audio, Archives, CAD)
- `ViewModels/MainViewModel.cs` — all app state and commands; `CurrentSearchMode` + `SearchModeDisplay`
- `Services/Database.cs` — schema, mode-aware `Search()`, 3-way import detection, `SaveAsSqliteAsync(mode)`, `SaveAsJsonAsync`
- `Services/FileScanner.cs` — async BFS enumeration; conditional text extraction based on mode
- `Services/TextExtractor.cs` — extracts text from PDF, DOCX, XLSX, PPTX, and 40+ plain-text/code extensions. Max 500k chars per file.
- `Services/ThemeManager.cs` — dark/light theme + all prefs (incl. `DefaultSearchMode`); persists to `%LocalAppData%/xDocHunter/prefs.json`. Also defines the `RecentIndexFile` record.
- `Views/AboutDialog.xaml` — app version/credits dialog
- `Views/ScanOptionsDialog.xaml` — search-mode radio group at top, file-type presets, custom extensions; also used in update-index flow (replaced ConfirmUpdateDialog)
- `Views/SettingsDialog.xaml` — all togglable settings + default search mode
- `Views/WelcomeView.xaml` — landing page
- `Views/PdfViewerWindow.xaml` — in-app WebView2-backed PDF viewer (read-only)
- `MainWindow.xaml` — main app layout (header, toolbar+stats, search, folder tree, results grid, footer with mode badge)
- `Assets/Icons/` — PNG icon assets registered as WPF `<Resource>` in `xDocHunter.csproj`
- `Fonts/` — custom TTF fonts bundled as `<Resource>`; referenced via `UiFont` / `MonoFont` dynamic resources

### Key NuGet packages

- `PdfPig` — PDF text extraction (used by `TextExtractor`)
- `DocumentFormat.OpenXml` — DOCX/XLSX/PPTX extraction (used by `TextExtractor`)
- `Microsoft.Data.Sqlite` — SQLite access; WAL + FTS5 enabled at DB init
- `CommunityToolkit.Mvvm` — source-generator MVVM (`[ObservableProperty]`, `[RelayCommand]`)
- `Microsoft.Web.WebView2` — embedded PDF viewer control

### Theming

Two complete ResourceDictionary files (`Themes/DarkTheme.xaml`, `Themes/LightTheme.xaml`) define all colors, button styles, DataGrid styles, and the ToggleSwitch style. Both files must be kept in sync — any style added to one must be added to the other.

### Built-in PDF viewer

`.pdf` results open inside the app via `Views/PdfViewerWindow`, which hosts a `Microsoft.Web.WebView2.Wpf.WebView2` control. Gated by the `UseBuiltInPdfViewer` pref (default **on**). Falls back to shell-execute if WebView2 is unavailable.

When **Custom PDF mouse controls** is enabled (`customPdfMouseControls` pref), `PdfViewerWindow` installs a `WH_MOUSE_LL` low-level mouse hook that intercepts clicks over the WebView2 area: left click = zoom in, right click = zoom out, middle click = enter/exit pan mode (scrolls via `SendInput` fired on a thread-pool thread to avoid hook re-entrancy deadlock). The WebView2 bounds are cached by a 150 ms `DispatcherTimer` to avoid calling `PointToScreen` from the hook callback. The hook is installed after `EnsureCoreWebView2Async` completes and removed on window close.

### Settings (ThemeManager prefs)

| Key | Default | Description |
|---|---|---|
| `theme` | `light` | Dark/light mode |
| `expandTreeOnLoad` | `true` | Auto-expand folder tree |
| `includeFilenameInSearch` | `true` | In Content mode, also matches against `name` (FTS5) |
| `defaultSearchMode` | `Filename` | Pre-selected mode in ScanOptionsDialog |
| `useBuiltInPdfViewer` | `true` | Open PDFs in WebView2 viewer |
| `customPdfMouseControls` | `false` | Enable custom mouse bindings in PDF viewer (zoom/pan); disabled when viewer is off |
| `trimPathEnabled` | `false` | Strip a prefix from displayed paths |
| `trimPathValue` | `""` | The prefix to strip |

### Local storage

- Index database: `%LocalAppData%/xDocHunter/index.db`
- User preferences: `%LocalAppData%/xDocHunter/prefs.json`
- WebView2 user-data folder: `%LocalAppData%/xDocHunter/WebView2`

## Critical Constraint

**xDocHunter is strictly read-only.** It only scans, indexes, searches, and views files. Never add features that modify, delete, rename, move, or overwrite user files. File interactions are limited to: open (shell execute or in-app PDF viewer), reveal in Explorer, and copy path to clipboard.
