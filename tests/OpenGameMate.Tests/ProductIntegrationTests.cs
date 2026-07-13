using System.IO.Compression;
using OpenGameMate.Core;
using OpenGameMate.Diagnostics;

namespace OpenGameMate.Tests;

public sealed class ProductIntegrationTests
{
    [Theory]
    [InlineData(CompanionPromptLanguage.ChineseSimplified)]
    [InlineData(CompanionPromptLanguage.English)]
    public void CompanionPrompts_ProvideAllProductMessages(CompanionPromptLanguage language)
    {
        Assert.NotEmpty(CompanionPrompts.FullRole(language));
        Assert.NotEmpty(CompanionPrompts.ShortReminder(language));
        Assert.NotEmpty(CompanionPrompts.AutomaticScreenshot(language));
        Assert.NotEmpty(CompanionPrompts.ManualScreenshot(language));
        Assert.NotEqual(
            CompanionPrompts.AutomaticScreenshot(language),
            CompanionPrompts.ManualScreenshot(language));
        Assert.InRange(CompanionPrompts.FullRole(language).Length, 1, 8000);
    }

    [Fact]
    public async Task DiagnosticExport_ContainsOnlySafeLogFileNames()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"opengamemate-export-{Guid.NewGuid():N}");
        var logsDirectory = Path.Combine(testRoot, "logs");
        var exportPath = Path.Combine(testRoot, "diagnostics.zip");
        var logPath = Path.Combine(logsDirectory, "opengamemate-20260714.jsonl");
        try
        {
            var logger = new JsonLineDiagnosticLogger(logsDirectory);
            await logger.AppendAsync(new DiagnosticEvent(
                DateTimeOffset.Parse("2026-07-14T10:00:00Z"),
                DiagnosticLevel.Information,
                "test.event",
                GameMateState.Idle,
                Success: true));

            var count = DiagnosticExportService.Export(logsDirectory, exportPath);

            Assert.Equal(1, count);
            using var archive = ZipFile.OpenRead(exportPath);
            var entry = Assert.Single(archive.Entries);
            Assert.Equal("opengamemate-20260714.jsonl", entry.FullName);
            Assert.DoesNotContain(testRoot, entry.FullName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteSingleFile(logPath);
            DeleteSingleFile(exportPath);
            DeleteEmptyDirectory(logsDirectory);
            DeleteEmptyDirectory(testRoot);
        }
    }

    private static void DeleteSingleFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteEmptyDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path);
        }
    }
}
