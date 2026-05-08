using System.IO;
using xDocHunter.Models;

namespace xDocHunter.Services;

public sealed class FileScanner
{
    public async IAsyncEnumerable<FileEntry> ScanAsync(
        string rootPath,
        ScanFilterOptions? filter = null,
        IProgress<ScanProgress>? progress = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException(rootPath);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        int count = 0;
        string lastDir = rootPath;
        const int progressEvery = 200;

        // Enumerate directories in a separate pass so they're indexed too.
        var dirQueue = new Queue<string>();
        dirQueue.Enqueue(rootPath);

        while (dirQueue.Count > 0 && !ct.IsCancellationRequested)
        {
            string dir = dirQueue.Dequeue();
            lastDir = dir;

            IEnumerable<FileSystemInfo> children;
            try
            {
                children = new DirectoryInfo(dir).EnumerateFileSystemInfos("*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = false,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint,
                        ReturnSpecialDirectories = false
                    });
            }
            catch { continue; }

            foreach (var fsi in children)
            {
                if (ct.IsCancellationRequested) yield break;

                if (fsi is DirectoryInfo di)
                {
                    dirQueue.Enqueue(di.FullName);
                    if (filter == null || filter.ScanAll)
                        yield return new FileEntry
                        {
                            FullPath = di.FullName,
                            Name = di.Name,
                            Directory = di.Parent?.FullName ?? "",
                            Extension = "",
                            SizeBytes = 0,
                            ModifiedUtc = di.LastWriteTimeUtc,
                            IsDirectory = true
                        };
                }
                else if (fsi is FileInfo fi)
                {
                    var ext = fi.Extension.ToLowerInvariant();

                    if (filter != null && !filter.ScanAll && !filter.AllowedExtensions.Contains(ext))
                        continue;

                    long size;
                    DateTime mod;
                    try { size = fi.Length; mod = fi.LastWriteTimeUtc; }
                    catch { size = 0; mod = default; }

                    var extractContent = (filter?.Mode ?? SearchMode.Filename) == SearchMode.Content;
                    var content = extractContent && TextExtractor.IsSupported(ext)
                        ? TextExtractor.Extract(fi.FullName, ext)
                        : string.Empty;

                    yield return new FileEntry
                    {
                        FullPath = fi.FullName,
                        Name = fi.Name,
                        Directory = fi.DirectoryName ?? "",
                        Extension = ext,
                        SizeBytes = size,
                        ModifiedUtc = mod,
                        IsDirectory = false,
                        Content = content
                    };
                }

                if (++count % progressEvery == 0)
                    progress?.Report(new ScanProgress(count, lastDir));
            }

            // yield briefly so UI stays responsive
            await Task.Yield();
        }

        progress?.Report(new ScanProgress(count, lastDir));
    }
}
