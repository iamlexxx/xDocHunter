namespace xDocHunter.Models;

public sealed class FileTypePreset
{
    public required string Name { get; init; }
    public required string[] Extensions { get; init; }
    public bool IsSelected { get; set; }

    // Whether content can be extracted from these files (used to dim presets in filename mode)
    public bool IsContentExtractable { get; init; }

    public string ExtensionsSummary => string.Join(" ", Extensions.Take(4)) +
        (Extensions.Length > 4 ? " …" : "");

    public static List<FileTypePreset> Defaults() =>
    [
        new() { Name = "PDFs",     Extensions = [".pdf"],                                                                                                  IsContentExtractable = true  },
        new() { Name = "Office",   Extensions = [".docx", ".xlsx", ".pptx"],                                                                               IsContentExtractable = true  },
        new() { Name = "Text",     Extensions = [".txt", ".md", ".markdown", ".log", ".csv", ".tsv"],                                                      IsContentExtractable = true  },
        new() { Name = "Code",     Extensions = [".cs", ".js", ".ts", ".jsx", ".tsx", ".py", ".java", ".cpp", ".c", ".h", ".go", ".rs", ".rb", ".php", ".sql", ".vb", ".fs"], IsContentExtractable = true  },
        new() { Name = "Web",      Extensions = [".html", ".htm", ".xhtml", ".css", ".scss", ".sass", ".less"],                                            IsContentExtractable = true  },
        new() { Name = "Config",   Extensions = [".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".config", ".properties", ".env"],    IsContentExtractable = true  },
        new() { Name = "Scripts",  Extensions = [".sh", ".bash", ".zsh", ".bat", ".cmd", ".ps1"],                                                          IsContentExtractable = true  },
        new() { Name = "Images",   Extensions = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp", ".svg", ".ico"],                               IsContentExtractable = false },
        new() { Name = "Videos",   Extensions = [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v"],                                         IsContentExtractable = false },
        new() { Name = "Audio",    Extensions = [".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a"],                                                 IsContentExtractable = false },
        new() { Name = "Archives", Extensions = [".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz"],                                                     IsContentExtractable = false },
        new() { Name = "CAD",      Extensions = [".dwg", ".dxf", ".step", ".stp", ".stl", ".iges", ".igs"],                                                IsContentExtractable = false },
    ];
}

public sealed class ScanFilterOptions
{
    public HashSet<string> AllowedExtensions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ScanAll => AllowedExtensions.Count == 0;
    public SearchMode Mode { get; init; } = SearchMode.Filename;
}
