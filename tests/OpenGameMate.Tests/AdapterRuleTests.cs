using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenGameMate.Adapters;

namespace OpenGameMate.Tests;

public sealed class AdapterRuleTests
{
    [Fact]
    public void BuiltInRules_AreValidAndContainOnlySelectors()
    {
        var rules = ChatGptAdapterRules.BuiltIn;

        rules.Validate();

        Assert.Equal(ChatGptAdapterRules.CurrentSchemaVersion, rules.SchemaVersion);
        Assert.NotEmpty(rules.ComposerSelector);
        Assert.NotEmpty(rules.SendButtonSelector);
        Assert.Equal("button[data-testid=\"stop-button\"]", rules.BusyButtonSelector);
        Assert.DoesNotContain("function", JsonSerializer.Serialize(rules), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://raw.githubusercontent.com/OpenGameMate/OpenGameMate/main/adapter-rules/chatgpt-v1.signed.json", true)]
    [InlineData("http://raw.githubusercontent.com/OpenGameMate/OpenGameMate/main/adapter-rules/chatgpt-v1.signed.json", false)]
    [InlineData("https://raw.githubusercontent.com/OpenGameMate/Other/main/adapter-rules/chatgpt-v1.signed.json", false)]
    [InlineData("https://raw.githubusercontent.com.evil.example/OpenGameMate/OpenGameMate/main/adapter-rules/chatgpt-v1.signed.json", false)]
    [InlineData("https://raw.githubusercontent.com/OpenGameMate/OpenGameMate/main/adapter-rules/chatgpt-v1.signed.json?ref=other", false)]
    [InlineData("https://raw.githubusercontent.com/OpenGameMate/OpenGameMate/dev/adapter-rules/chatgpt-v1.signed.json", false)]
    public void OfficialRuleSourcePolicy_IsExactAndFailClosed(string value, bool expected)
    {
        var policy = new OfficialGitHubRuleSourcePolicy("OpenGameMate", "OpenGameMate");

        Assert.Equal(expected, policy.IsAllowed(new Uri(value)));
    }

    [Fact]
    public void SignedRuleDocument_WithValidSignature_IsAccepted()
    {
        using var signingKey = RSA.Create(2048);
        using var verifier = new RsaPssAdapterRuleVerifier(signingKey.ExportSubjectPublicKeyInfoPem());
        var document = CreateSignedDocument(signingKey, JsonSerializer.SerializeToUtf8Bytes(
            ChatGptAdapterRules.BuiltIn with { RulesVersion = "remote-1" }));

        var result = RemoteAdapterRuleLoader.ValidateDocument(document, verifier);

        Assert.True(result.UsedRemoteRules);
        Assert.Equal(AdapterRuleLoadCode.RemoteAccepted, result.Code);
        Assert.Equal("remote-1", result.Rules.RulesVersion);
    }

    [Fact]
    public void SignedRuleDocument_WithTamperedPayload_FallsBackToBuiltInRules()
    {
        using var signingKey = RSA.Create(2048);
        using var verifier = new RsaPssAdapterRuleVerifier(signingKey.ExportSubjectPublicKeyInfoPem());
        var originalPayload = JsonSerializer.SerializeToUtf8Bytes(
            ChatGptAdapterRules.BuiltIn with { RulesVersion = "remote-1" });
        var signature = signingKey.SignData(
            originalPayload,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        var tamperedPayload = JsonSerializer.SerializeToUtf8Bytes(
            ChatGptAdapterRules.BuiltIn with { RulesVersion = "tampered" });
        var document = CreateEnvelope(tamperedPayload, signature);

        var result = RemoteAdapterRuleLoader.ValidateDocument(document, verifier);

        Assert.False(result.UsedRemoteRules);
        Assert.Equal(AdapterRuleLoadCode.SignatureInvalid, result.Code);
        Assert.Same(ChatGptAdapterRules.BuiltIn, result.Rules);
    }

    [Fact]
    public void SignedRuleDocument_WithUnknownPayloadField_IsRejectedAfterSignatureVerification()
    {
        using var signingKey = RSA.Create(2048);
        using var verifier = new RsaPssAdapterRuleVerifier(signingKey.ExportSubjectPublicKeyInfoPem());
        var payload = Encoding.UTF8.GetBytes(
            """
            {
              "schemaVersion": 1,
              "rulesVersion": "remote-1",
              "composerSelector": "#prompt-textarea",
              "fileInputSelector": "input[type=file]",
              "sendButtonSelector": "button[data-testid=send-button]",
              "attachmentPreviewSelectors": [],
              "quotaErrorSelectors": [],
              "platformErrorSelectors": [],
              "script": "alert(1)"
            }
            """);
        var document = CreateSignedDocument(signingKey, payload);

        var result = RemoteAdapterRuleLoader.ValidateDocument(document, verifier);

        Assert.False(result.UsedRemoteRules);
        Assert.Equal(AdapterRuleLoadCode.PayloadInvalid, result.Code);
        Assert.Same(ChatGptAdapterRules.BuiltIn, result.Rules);
    }

    [Fact]
    public void SignedRuleDocument_WithOutOfScopeComposerSelector_IsRejected()
    {
        using var signingKey = RSA.Create(2048);
        using var verifier = new RsaPssAdapterRuleVerifier(signingKey.ExportSubjectPublicKeyInfoPem());
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            ChatGptAdapterRules.BuiltIn with
            {
                RulesVersion = "remote-unsafe",
                ComposerSelector = "main article",
            });
        var document = CreateSignedDocument(signingKey, payload);

        var result = RemoteAdapterRuleLoader.ValidateDocument(document, verifier);

        Assert.False(result.UsedRemoteRules);
        Assert.Equal(AdapterRuleLoadCode.PayloadInvalid, result.Code);
    }

    [Fact]
    public async Task MissingOfficialVerifier_DisablesRemoteCheckWithoutNetworkAccess()
    {
        var handler = new CountingHandler();
        using var client = new HttpClient(handler);
        var loader = new RemoteAdapterRuleLoader(
            client,
            new OfficialGitHubRuleSourcePolicy("OpenGameMate", "OpenGameMate"),
            signatureVerifier: null);

        var result = await loader.LoadAsync(new Uri(
            "https://raw.githubusercontent.com/OpenGameMate/OpenGameMate/main/adapter-rules/chatgpt-v1.signed.json"));

        Assert.Equal(AdapterRuleLoadCode.RemoteDisabled, result.Code);
        Assert.False(result.UsedRemoteRules);
        Assert.Equal(0, handler.RequestCount);
    }

    private static byte[] CreateSignedDocument(RSA signingKey, byte[] payload)
    {
        var signature = signingKey.SignData(
            payload,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        return CreateEnvelope(payload, signature);
    }

    private static byte[] CreateEnvelope(byte[] payload, byte[] signature) =>
        JsonSerializer.SerializeToUtf8Bytes(new SignedAdapterRuleEnvelope
        {
            Algorithm = SignedAdapterRuleEnvelope.RequiredAlgorithm,
            Payload = Convert.ToBase64String(payload),
            Signature = Convert.ToBase64String(signature),
        });

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
