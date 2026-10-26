using System.Text.Json;
using OpenGameMate.Adapters;
using OpenGameMate.Core;
using OpenGameMate.Diagnostics;

namespace OpenGameMate.Tests;

public sealed class AttachmentDiagnosticTests
{
    [Fact]
    public async Task IdentityDiagnostics_PersistPostProofEnumsAndDropUntrustedFields()
    {
        using var response = JsonDocument.Parse("""
            {"identityDiagnostics":{"mime":"Png","stage":"Digest","result":"Error","sourceRejectReason":"None",
            "proofFailure":"Digest","exceptionKind":"OperationError","duration":"Under2Seconds","sourceCount":2,
            "dimensionsEqual":true,"originalFileVerified":true,"uri":"PRIVATE_URI","message":"PRIVATE_MESSAGE","hash":"PRIVATE_HASH"}}
            """);
        var identity = Assert.IsType<AdapterIdentityDiagnostics>(ChatGptWebAdapter.ParseIdentityDiagnostics(response.RootElement));
        var directory = Path.Combine(Path.GetTempPath(), $"ogm-identity-logs-{Guid.NewGuid():N}");
        var file = Path.Combine(directory, "opengamemate-20261026.jsonl");
        try
        {
            await new JsonLineDiagnosticLogger(directory).AppendAsync(new DiagnosticEvent(
                new DateTimeOffset(2026, 10, 26, 15, 0, 0, TimeSpan.Zero), DiagnosticLevel.Warning,
                "adapter.submit-probe", ErrorCode: "prepared-attachment-local-digest-failed", Success: false,
                AdapterDiagnostics: new(AdapterPageState.ComposerWithAttachment, 0, [], AdapterFailureStage.Invocation, Identity: identity)));
            var line = Assert.Single(await File.ReadAllLinesAsync(file));
            using var saved = JsonDocument.Parse(line);
            var result = saved.RootElement.GetProperty("adapterDiagnostics").GetProperty("identity");
            Assert.Equal("digest", result.GetProperty("proofFailure").GetString());
            Assert.Equal("operationError", result.GetProperty("exceptionKind").GetString());
            Assert.Equal("error", result.GetProperty("result").GetString());
            Assert.True(result.GetProperty("dimensionsEqual").GetBoolean());
            Assert.DoesNotContain("PRIVATE", line, StringComparison.Ordinal);
            Assert.Throws<DiagnosticEventValidationException>(() => new DiagnosticEvent(DateTimeOffset.UtcNow,
                DiagnosticLevel.Warning, "adapter.submit-probe", AdapterDiagnostics:
                    new(AdapterPageState.Unknown, 0, [], AdapterFailureStage.Invocation,
                        Identity: identity with { ExceptionKind = (IdentityExceptionKind)999 })).Validate());
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task AttachmentDiagnostics_PersistOnlyFiniteCategoriesAndLifecycleFlags()
    {
        using var response = JsonDocument.Parse("""
            {"attachmentDiagnostics":{"beforeCount":1,"afterCount":1,
            "beforeSrc":"Blob","afterSrc":"Http","beforeSrcSet":"Empty","afterSrcSet":"Http",
            "beforeCurrentSrc":"Blob","afterCurrentSrc":"Http","srcChanged":true,"srcSetChanged":true,
            "currentSrcChanged":true,"ariaBusy":false,"progressBarVisible":false,"busyObserved":true,
            "completionObserved":true,"uploadPhase":"Ready","url":"https://example.invalid/PRIVATE_SOURCE"}}
            """);
        var attachment = ChatGptWebAdapter.ParseAttachmentDiagnostics(response.RootElement);
        var directory = Path.Combine(Path.GetTempPath(), $"ogm-source-diagnostics-{Guid.NewGuid():N}");
        var file = Path.Combine(directory, "opengamemate-20261026.jsonl");
        try
        {
            await new JsonLineDiagnosticLogger(directory).AppendAsync(new DiagnosticEvent(
                new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero), DiagnosticLevel.Warning,
                "adapter.submit-probe", ErrorCode: "prepared-attachment-source-changed", Success: false,
                AdapterDiagnostics: new(AdapterPageState.ComposerWithAttachment, 0, [], AdapterFailureStage.Invocation, attachment)));
            var line = Assert.Single(await File.ReadAllLinesAsync(file));
            using var document = JsonDocument.Parse(line);
            var diagnostics = document.RootElement.GetProperty("adapterDiagnostics");
            var saved = diagnostics.GetProperty("attachment");
            Assert.Equal("invocation", diagnostics.GetProperty("failureStage").GetString());
            Assert.Equal("blob", saved.GetProperty("beforeSrc").GetString());
            Assert.Equal("http", saved.GetProperty("afterSrc").GetString());
            Assert.Equal("http", saved.GetProperty("afterSrcSet").GetString());
            Assert.True(saved.GetProperty("srcChanged").GetBoolean());
            Assert.True(saved.GetProperty("srcSetChanged").GetBoolean());
            Assert.True(saved.GetProperty("completionObserved").GetBoolean());
            Assert.False(saved.GetProperty("ariaBusy").GetBoolean());
            Assert.DoesNotContain("PRIVATE_SOURCE", line, StringComparison.Ordinal);
            Assert.DoesNotContain("https:", line, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Fact]
    public void AttachmentParser_RejectsFreeTextAndUndefinedEnums()
    {
        using var response = JsonDocument.Parse("""
            {"attachmentDiagnostics":{"beforeCount":-1,"afterCount":999,"beforeSrc":"https://example.invalid/PRIVATE_SOURCE",
            "afterSrc":"999","uploadPhase":"PRIVATE_TEXT","ariaBusy":"PRIVATE_TEXT"}}
            """);
        var attachment = Assert.IsType<AdapterAttachmentDiagnostics>(ChatGptWebAdapter.ParseAttachmentDiagnostics(response.RootElement));
        Assert.Equal(AttachmentSourceKind.Unknown, attachment.BeforeSrc);
        Assert.Equal(AttachmentSourceKind.Unknown, attachment.AfterSrc);
        Assert.Equal(AttachmentUploadPhase.Unknown, attachment.UploadPhase);
        Assert.Null(attachment.AriaBusy);
        Assert.Equal(0, attachment.BeforeCount);
        Assert.Equal(0, attachment.AfterCount);
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(attachment), StringComparison.Ordinal);
    }

    [Fact]
    public void AttachmentLogger_RejectsInvalidCountsAndEnumValues()
    {
        var attachment = new AdapterAttachmentDiagnostics(1, 1, AttachmentSourceKind.Blob, AttachmentSourceKind.Http,
            AttachmentSourceKind.Empty, AttachmentSourceKind.Empty, AttachmentSourceKind.Blob, AttachmentSourceKind.Http,
            true, false, true, null, false, true, false, AttachmentUploadPhase.Uploading);
        foreach (var invalid in new[] { attachment with { AfterCount = 100 }, attachment with { AfterSrc = (AttachmentSourceKind)999 } })
        {
            Assert.Throws<DiagnosticEventValidationException>(() => new DiagnosticEvent(DateTimeOffset.UtcNow,
                DiagnosticLevel.Warning, "adapter.input-prepared", AdapterDiagnostics:
                    new(AdapterPageState.Unknown, 0, [], AdapterFailureStage.AttachmentPreparation, invalid)).Validate());
        }
    }
}
