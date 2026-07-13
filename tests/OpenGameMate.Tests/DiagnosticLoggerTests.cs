using System.Text.Json;
using OpenGameMate.Core;
using OpenGameMate.Diagnostics;

namespace OpenGameMate.Tests;

public sealed class DiagnosticLoggerTests
{
    [Fact]
    public async Task Logger_WritesOnlyAllowlistedStructuredFields()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"ogm-logs-{Guid.NewGuid():N}");
        var logFile = Path.Combine(testDirectory, "opengamemate-20260714.jsonl");
        try
        {
            var logger = new JsonLineDiagnosticLogger(testDirectory);
            await logger.AppendAsync(new DiagnosticEvent(
                new DateTimeOffset(2026, 7, 14, 1, 2, 3, TimeSpan.Zero),
                DiagnosticLevel.Information,
                "capture.completed",
                GameMateState.Running,
                Success: true,
                ImageWidth: 1728,
                ImageHeight: 1080,
                FileSizeBytes: 978113));

            var line = Assert.Single(await File.ReadAllLinesAsync(logFile));
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            Assert.Equal("capture.completed", root.GetProperty("eventName").GetString());
            Assert.Equal("running", root.GetProperty("state").GetString());
            Assert.Equal(1728, root.GetProperty("imageWidth").GetInt32());
            Assert.False(root.TryGetProperty("message", out _));
            Assert.False(root.TryGetProperty("path", out _));
            Assert.False(root.TryGetProperty("detail", out _));
        }
        finally
        {
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }

            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory);
            }
        }
    }

    [Fact]
    public async Task Logger_RejectsFreeFormEventNames()
    {
        var logger = new JsonLineDiagnosticLogger(Path.Combine(Path.GetTempPath(), "unused-ogm-log"));
        var diagnosticEvent = new DiagnosticEvent(
            DateTimeOffset.UtcNow,
            DiagnosticLevel.Error,
            "contains private free form text");

        await Assert.ThrowsAsync<DiagnosticEventValidationException>(
            () => logger.AppendAsync(diagnosticEvent));
    }
}
