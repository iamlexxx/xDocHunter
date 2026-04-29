using xDocHunter.Models;
using Xunit;

namespace xDocHunter.Tests;

public class ModelsTests
{
    [Fact]
    public void FileTypePreset_Defaults_HasAllTwelvePresets()
    {
        var presets = FileTypePreset.Defaults();
        Assert.Equal(12, presets.Count);

        var names = presets.Select(p => p.Name).ToHashSet();
        var expected = new[] { "PDFs", "Office", "Text", "Code", "Web", "Config", "Scripts",
                               "Images", "Videos", "Audio", "Archives", "CAD" };
        foreach (var name in expected)
            Assert.Contains(name, names);
    }

    [Fact]
    public void FileTypePreset_ContentExtractableFlag_IsCorrect()
    {
        var presets = FileTypePreset.Defaults().ToDictionary(p => p.Name);

        // These should be content-extractable (TextExtractor supports their extensions)
        Assert.True(presets["PDFs"].IsContentExtractable);
        Assert.True(presets["Office"].IsContentExtractable);
        Assert.True(presets["Text"].IsContentExtractable);
        Assert.True(presets["Code"].IsContentExtractable);
        Assert.True(presets["Web"].IsContentExtractable);
        Assert.True(presets["Config"].IsContentExtractable);
        Assert.True(presets["Scripts"].IsContentExtractable);

        // These should NOT be content-extractable
        Assert.False(presets["Images"].IsContentExtractable);
        Assert.False(presets["Videos"].IsContentExtractable);
        Assert.False(presets["Audio"].IsContentExtractable);
        Assert.False(presets["Archives"].IsContentExtractable);
        Assert.False(presets["CAD"].IsContentExtractable);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024L * 1024, "1 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    [InlineData(1024L * 1024 * 1024 * 1024, "1 TB")]
    public void FileEntry_FormatSize_FormatsCorrectly(long bytes, string expected)
    {
        // FormatSize is private; exercise it through the public SizeDisplay property.
        var entry = new FileEntry
        {
            FullPath    = @"C:\f.txt",
            Name        = "f.txt",
            Directory   = @"C:\",
            Extension   = ".txt",
            SizeBytes   = bytes,
            ModifiedUtc = DateTime.UtcNow,
            IsDirectory = false
        };
        Assert.Equal(expected, entry.SizeDisplay);
    }

    [Fact]
    public void ScanFilterOptions_ScanAll_TrueWhenNoExtensions()
    {
        var opts = new ScanFilterOptions();
        Assert.True(opts.ScanAll);
        Assert.Equal(SearchMode.Filename, opts.Mode); // default mode
    }

    [Fact]
    public void ScanFilterOptions_ScanAll_FalseWhenExtensionsAdded()
    {
        var opts = new ScanFilterOptions { Mode = SearchMode.Content };
        opts.AllowedExtensions.Add(".pdf");
        Assert.False(opts.ScanAll);
        Assert.Equal(SearchMode.Content, opts.Mode);
    }

    [Fact]
    public void ScanFilterOptions_AllowedExtensions_IsCaseInsensitive()
    {
        var opts = new ScanFilterOptions();
        opts.AllowedExtensions.Add(".PDF");
        Assert.Contains(".pdf", opts.AllowedExtensions);
        Assert.Contains(".PDF", opts.AllowedExtensions);
    }

    [Fact]
    public void FileEntry_DisplayProperties_FormatCorrectly()
    {
        var f = new FileEntry
        {
            FullPath = @"C:\foo\bar.txt",
            Name = "bar.txt",
            Directory = @"C:\foo",
            Extension = ".txt",
            SizeBytes = 2048,
            ModifiedUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            IsDirectory = false
        };
        Assert.Equal("2 KB", f.SizeDisplay);
        Assert.Contains("txt", f.TypeDisplay, StringComparison.OrdinalIgnoreCase);
    }
}
