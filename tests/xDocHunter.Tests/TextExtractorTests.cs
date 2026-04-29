using System.IO;
using xDocHunter.Services;
using Xunit;

namespace xDocHunter.Tests;

public class TextExtractorTests : IDisposable
{
    private readonly TestSandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    // ── IsSupported ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(".txt",  true)]
    [InlineData(".md",   true)]
    [InlineData(".cs",   true)]
    [InlineData(".py",   true)]
    [InlineData(".json", true)]
    [InlineData(".xml",  true)]
    [InlineData(".html", true)]
    [InlineData(".sql",  true)]
    [InlineData(".sh",   true)]
    [InlineData(".ps1",  true)]
    [InlineData(".pdf",  true)]
    [InlineData(".docx", true)]
    [InlineData(".xlsx", true)]
    [InlineData(".pptx", true)]
    public void IsSupported_SupportedExtensions_ReturnsTrue(string ext, bool expected)
    {
        Assert.Equal(expected, TextExtractor.IsSupported(ext));
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".png")]
    [InlineData(".mp4")]
    [InlineData(".zip")]
    [InlineData(".exe")]
    [InlineData(".dll")]
    [InlineData(".dwg")]
    public void IsSupported_UnsupportedExtensions_ReturnsFalse(string ext)
    {
        Assert.False(TextExtractor.IsSupported(ext));
    }

    [Theory]
    [InlineData(".TXT")]
    [InlineData(".MD")]
    [InlineData(".CS")]
    [InlineData(".PDF")]
    public void IsSupported_IsCaseInsensitive(string ext)
    {
        Assert.True(TextExtractor.IsSupported(ext));
    }

    // ── Plain text extraction ────────────────────────────────────────────────

    [Fact]
    public void Extract_PlainTextFile_ReturnsContent()
    {
        var path = _sandbox.WriteFile("hello.txt", "Hello, world!");
        var result = TextExtractor.Extract(path, ".txt");
        Assert.Equal("Hello, world!", result);
    }

    [Fact]
    public void Extract_MarkdownFile_ReturnsContent()
    {
        var content = "# Title\n\nSome **bold** text.";
        var path = _sandbox.WriteFile("readme.md", content);
        var result = TextExtractor.Extract(path, ".md");
        Assert.Equal(content, result);
    }

    [Fact]
    public void Extract_CSharpFile_ReturnsContent()
    {
        var content = "public class Foo { }";
        var path = _sandbox.WriteFile("Foo.cs", content);
        Assert.Equal(content, TextExtractor.Extract(path, ".cs"));
    }

    [Fact]
    public void Extract_JsonFile_ReturnsContent()
    {
        var content = "{\"key\":\"value\"}";
        var path = _sandbox.WriteFile("data.json", content);
        Assert.Equal(content, TextExtractor.Extract(path, ".json"));
    }

    [Fact]
    public void Extract_EmptyFile_ReturnsEmptyString()
    {
        var path = _sandbox.WriteFile("empty.txt", "");
        Assert.Equal(string.Empty, TextExtractor.Extract(path, ".txt"));
    }

    // ── Binary detection ─────────────────────────────────────────────────────

    [Fact]
    public void Extract_FileWithNullBytes_ReturnsEmpty()
    {
        // Null byte anywhere in first 512 bytes triggers binary detection.
        var bytes = new byte[] { 0x48, 0x65, 0x00, 0x6C, 0x6C, 0x6F }; // "He\0llo"
        var path = _sandbox.WriteBytes("binary.txt", bytes);
        Assert.Equal(string.Empty, TextExtractor.Extract(path, ".txt"));
    }

    [Fact]
    public void Extract_FileWithNullByteAfter512Bytes_ReturnsContent()
    {
        // Null byte AFTER the 512-byte probe window → not detected as binary.
        var bytes = new byte[520];
        for (int i = 0; i < 512; i++) bytes[i] = (byte)'A';
        bytes[513] = 0x00; // beyond the probe window
        var path = _sandbox.WriteBytes("late_null.txt", bytes);
        var result = TextExtractor.Extract(path, ".txt");
        // Should NOT be empty — binary detection did not fire.
        Assert.NotEqual(string.Empty, result);
    }

    [Fact]
    public void Extract_AllNullBytes_ReturnsEmpty()
    {
        var path = _sandbox.WriteBytes("zeros.txt", new byte[64]);
        Assert.Equal(string.Empty, TextExtractor.Extract(path, ".txt"));
    }

    // ── MaxChars truncation ──────────────────────────────────────────────────

    [Fact]
    public void Extract_ContentExceedsMaxChars_TruncatesAt500000()
    {
        const int maxChars = 500_000;
        // Write a file that is longer than MaxChars.
        var content = new string('X', maxChars + 1000);
        var path = _sandbox.WriteFile("big.txt", content);
        var result = TextExtractor.Extract(path, ".txt");
        Assert.Equal(maxChars, result.Length);
        Assert.True(result.All(c => c == 'X'));
    }

    [Fact]
    public void Extract_ContentExactlyMaxChars_ReturnsFullContent()
    {
        const int maxChars = 500_000;
        var content = new string('Y', maxChars);
        var path = _sandbox.WriteFile("exact.txt", content);
        var result = TextExtractor.Extract(path, ".txt");
        Assert.Equal(maxChars, result.Length);
    }

    [Fact]
    public void Extract_ContentBelowMaxChars_ReturnsFullContent()
    {
        var content = new string('Z', 1000);
        var path = _sandbox.WriteFile("small.txt", content);
        var result = TextExtractor.Extract(path, ".txt");
        Assert.Equal(1000, result.Length);
    }

    // ── Unsupported extension ─────────────────────────────────────────────────

    [Fact]
    public void Extract_UnsupportedExtension_ReturnsEmpty()
    {
        // Even if the file exists, an extension not in either set returns empty.
        var path = _sandbox.WriteFile("image.jpg", "not really an image");
        Assert.Equal(string.Empty, TextExtractor.Extract(path, ".jpg"));
    }

    // ── Multiline and unicode ─────────────────────────────────────────────────

    [Fact]
    public void Extract_MultilineContent_PreservesNewlines()
    {
        var content = "line1\nline2\nline3";
        var path = _sandbox.WriteFile("multi.txt", content);
        Assert.Equal(content, TextExtractor.Extract(path, ".txt"));
    }

    [Fact]
    public void Extract_UnicodeContent_DecodesCorrectly()
    {
        var content = "Héllo Wörld – 日本語テスト";
        var path = _sandbox.WriteFile("unicode.txt", content);
        var result = TextExtractor.Extract(path, ".txt");
        Assert.Equal(content, result);
    }
}
