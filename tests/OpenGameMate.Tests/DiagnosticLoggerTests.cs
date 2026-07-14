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

    [Fact]
    public async Task Logger_PersistsBoundedAdapterDiagnosticsWithoutPageContent()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"ogm-adapter-logs-{Guid.NewGuid():N}");
        var logFile = Path.Combine(testDirectory, "opengamemate-20260714.jsonl");
        try
        {
            var logger = new JsonLineDiagnosticLogger(testDirectory);
            var adapterDiagnostics = new AdapterDiagnostics(
                AdapterPageState.VoiceComposerWithAttachment,
                2,
                [new AdapterButtonCandidate(
                    "button",
                    0,
                    "button",
                    null,
                    true,
                    false,
                    "发送提示",
                    "send-button")],
                AdapterFailureStage.ButtonValidation);

            await logger.AppendAsync(new DiagnosticEvent(
                new DateTimeOffset(2026, 7, 14, 1, 2, 3, TimeSpan.Zero),
                DiagnosticLevel.Warning,
                "adapter.submit-probe",
                GameMateState.Sending,
                ErrorCode: "send-control-timeout",
                Success: false,
                AdapterDiagnostics: adapterDiagnostics));

            var line = Assert.Single(await File.ReadAllLinesAsync(logFile));
            using var document = JsonDocument.Parse(line);
            var persisted = document.RootElement.GetProperty("adapterDiagnostics");
            Assert.Equal("voiceComposerWithAttachment", persisted.GetProperty("pageState").GetString());
            Assert.Equal(2, persisted.GetProperty("detectedButtonCount").GetInt32());
            Assert.Equal("send-button", persisted.GetProperty("candidateButtons")[0]
                .GetProperty("dataTestId").GetString());
            Assert.False(document.RootElement.TryGetProperty("pageText", out _));
            Assert.False(document.RootElement.TryGetProperty("reply", out _));
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
    public async Task Logger_PersistsPendingAndAudioTimingWithoutAudioOrPageContent()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"ogm-pending-logs-{Guid.NewGuid():N}");
        var logFile = Path.Combine(testDirectory, "opengamemate-20260714.jsonl");
        var scheduledAt = new DateTimeOffset(2026, 7, 14, 1, 0, 0, TimeSpan.Zero);
        try
        {
            var logger = new JsonLineDiagnosticLogger(testDirectory);
            await logger.AppendAsync(new DiagnosticEvent(
                scheduledAt.AddSeconds(93),
                DiagnosticLevel.Information,
                "pending.expired",
                GameMateState.Running,
                ErrorCode: "conversation-busy-timeout",
                ScheduledAt: scheduledAt,
                PendingCreatedAt: scheduledAt,
                DeferredAt: scheduledAt.AddSeconds(2),
                DeferredReason: "audio-playing",
                IdleCandidateAt: scheduledAt.AddSeconds(10),
                IdleStableDurationMs: 2500,
                AudioState: WebAudioState.Playing,
                AudioSilentDurationMs: 0,
                AttachStartedAt: scheduledAt.AddSeconds(20),
                TextSetAt: scheduledAt.AddSeconds(21),
                SubmitStartedAt: scheduledAt.AddSeconds(22),
                VoiceStateChangedAfterAttach: true,
                SkippedBecauseConversationBusy: true,
                PendingExpiredAt: scheduledAt.AddSeconds(90)));

            var line = Assert.Single(await File.ReadAllLinesAsync(logFile));
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            Assert.Equal("audio-playing", root.GetProperty("deferredReason").GetString());
            Assert.Equal("playing", root.GetProperty("audioState").GetString());
            Assert.Equal(2500, root.GetProperty("idleStableDurationMs").GetInt64());
            Assert.True(root.GetProperty("voiceStateChangedAfterAttach").GetBoolean());
            Assert.True(root.GetProperty("skippedBecauseConversationBusy").GetBoolean());
            Assert.False(root.TryGetProperty("audioContent", out _));
            Assert.False(root.TryGetProperty("pageContent", out _));
            Assert.False(root.TryGetProperty("screenshot", out _));
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
    public void DiagnosticEvent_RejectsInvalidPendingDurationsAndReasons()
    {
        Assert.Throws<DiagnosticEventValidationException>(() => new DiagnosticEvent(
            DateTimeOffset.UtcNow,
            DiagnosticLevel.Information,
            "pending.deferred",
            DeferredReason: "contains private text",
            IdleStableDurationMs: -1).Validate());
    }
}
