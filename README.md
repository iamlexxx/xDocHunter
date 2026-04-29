# xDocHunter

A WPF desktop app (.NET 8, win-x64) for indexing and searching files by **name** or **content**.

It extracts text from PDFs, Office documents, code files, and 40+ plain-text formats, then stores everything in a local SQLite index for fast full-text search.

## Features

- **Filename mode** — fast scan, search by file name and path
- **Content mode** — extracts and indexes text from PDFs, DOCX, XLSX, PPTX, and 40+ formats
- Save and load indexes as `.nfindex` (SQLite) or `.nfindex.json`
- Built-in PDF viewer (WebView2)
- Dark and light themes
- Folder tree sidebar, file-type presets, custom extension filters

## Build & Run

```bash
# Build
dotnet build src/xDocHunter/xDocHunter.csproj

# Run
dotnet run --project src/xDocHunter/xDocHunter.csproj

# Publish self-contained single-file exe
dotnet publish src/xDocHunter/xDocHunter.csproj -c Release
```

## Tech Stack

- .NET 8 / WPF (win-x64)
- SQLite with WAL + FTS5 (`Microsoft.Data.Sqlite`)
- MVVM via `CommunityToolkit.Mvvm`
- PDF extraction: `PdfPig`
- Office extraction: `DocumentFormat.OpenXml`
- PDF viewer: `Microsoft.Web.WebView2`

## License

MIT — see [LICENSE](LICENSE).
