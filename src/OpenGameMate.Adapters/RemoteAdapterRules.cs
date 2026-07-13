using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenGameMate.Adapters;

public enum AdapterRuleLoadCode
{
    RemoteAccepted,
    RemoteDisabled,
    SourceRejected,
    DownloadFailed,
    DocumentTooLarge,
    EnvelopeInvalid,
    SignatureInvalid,
    PayloadInvalid,
}

public sealed record AdapterRuleLoadResult(
    ChatGptAdapterRules Rules,
    bool UsedRemoteRules,
    AdapterRuleLoadCode Code);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SignedAdapterRuleEnvelope
{
    public const int CurrentSchemaVersion = 1;
    public const string RequiredAlgorithm = "RSA-PSS-SHA256";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string Algorithm { get; init; }

    public required string Payload { get; init; }

    public required string Signature { get; init; }
}

public sealed class OfficialGitHubRuleSourcePolicy
{
    private readonly string _expectedPath;

    public OfficialGitHubRuleSourcePolicy(string repositoryOwner, string repositoryName)
    {
        ValidateRepositoryToken(repositoryOwner, nameof(repositoryOwner));
        ValidateRepositoryToken(repositoryName, nameof(repositoryName));
        _expectedPath = $"/{repositoryOwner}/{repositoryName}/main/adapter-rules/chatgpt-v1.signed.json";
    }

    public bool IsAllowed(Uri? source) =>
        source is
        {
            IsAbsoluteUri: true,
            IsDefaultPort: true,
            Scheme: "https",
            Host: "raw.githubusercontent.com",
            Query.Length: 0,
            Fragment.Length: 0,
        } && source.AbsolutePath.Equals(_expectedPath, StringComparison.Ordinal);

    private static void ValidateRepositoryToken(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 100 ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new ArgumentException("Invalid GitHub repository token.", name);
        }
    }
}

public sealed class RsaPssAdapterRuleVerifier : IDisposable
{
    private readonly RSA _rsa = RSA.Create();

    public RsaPssAdapterRuleVerifier(string subjectPublicKeyInfoPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectPublicKeyInfoPem);
        _rsa.ImportFromPem(subjectPublicKeyInfoPem);
    }

    public bool Verify(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature) =>
        _rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);

    public void Dispose() => _rsa.Dispose();
}

public sealed class RemoteAdapterRuleLoader
{
    public const int MaximumDocumentBytes = 64 * 1024;
    public const int MaximumPayloadBytes = 32 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly HttpClient _httpClient;
    private readonly OfficialGitHubRuleSourcePolicy _sourcePolicy;
    private readonly RsaPssAdapterRuleVerifier? _signatureVerifier;

    public RemoteAdapterRuleLoader(
        HttpClient httpClient,
        OfficialGitHubRuleSourcePolicy sourcePolicy,
        RsaPssAdapterRuleVerifier? signatureVerifier)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(sourcePolicy);
        _httpClient = httpClient;
        _sourcePolicy = sourcePolicy;
        _signatureVerifier = signatureVerifier;
    }

    public async Task<AdapterRuleLoadResult> LoadAsync(
        Uri source,
        CancellationToken cancellationToken = default)
    {
        if (_signatureVerifier is null)
        {
            return Fallback(AdapterRuleLoadCode.RemoteDisabled);
        }

        if (!_sourcePolicy.IsAllowed(source))
        {
            return Fallback(AdapterRuleLoadCode.SourceRejected);
        }

        byte[] document;
        try
        {
            using var response = await _httpClient.GetAsync(
                source,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK ||
                !_sourcePolicy.IsAllowed(response.RequestMessage?.RequestUri))
            {
                return Fallback(AdapterRuleLoadCode.DownloadFailed);
            }

            if (response.Content.Headers.ContentLength > MaximumDocumentBytes)
            {
                return Fallback(AdapterRuleLoadCode.DocumentTooLarge);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            document = await ReadBoundedAsync(stream, MaximumDocumentBytes, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AdapterRuleDocumentTooLargeException)
        {
            return Fallback(AdapterRuleLoadCode.DocumentTooLarge);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or TaskCanceledException)
        {
            return Fallback(AdapterRuleLoadCode.DownloadFailed);
        }

        return ValidateDocument(document, _signatureVerifier);
    }

    public static AdapterRuleLoadResult ValidateDocument(
        ReadOnlySpan<byte> document,
        RsaPssAdapterRuleVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        if (document.Length is 0 or > MaximumDocumentBytes)
        {
            return Fallback(AdapterRuleLoadCode.DocumentTooLarge);
        }

        SignedAdapterRuleEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<SignedAdapterRuleEnvelope>(document, JsonOptions);
        }
        catch (JsonException)
        {
            return Fallback(AdapterRuleLoadCode.EnvelopeInvalid);
        }

        if (envelope is null ||
            envelope.SchemaVersion != SignedAdapterRuleEnvelope.CurrentSchemaVersion ||
            !envelope.Algorithm.Equals(SignedAdapterRuleEnvelope.RequiredAlgorithm, StringComparison.Ordinal))
        {
            return Fallback(AdapterRuleLoadCode.EnvelopeInvalid);
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload = Convert.FromBase64String(envelope.Payload);
            signature = Convert.FromBase64String(envelope.Signature);
        }
        catch (FormatException)
        {
            return Fallback(AdapterRuleLoadCode.EnvelopeInvalid);
        }

        if (payload.Length is 0 or > MaximumPayloadBytes || signature.Length is 0 or > 1024)
        {
            return Fallback(AdapterRuleLoadCode.EnvelopeInvalid);
        }

        bool signatureValid;
        try
        {
            signatureValid = verifier.Verify(payload, signature);
        }
        catch (CryptographicException)
        {
            signatureValid = false;
        }

        if (!signatureValid)
        {
            return Fallback(AdapterRuleLoadCode.SignatureInvalid);
        }

        try
        {
            var rules = JsonSerializer.Deserialize<ChatGptAdapterRules>(payload, JsonOptions)
                ?? throw new AdapterRuleValidationException("Adapter-rule payload was empty.");
            rules.Validate();
            return new(rules, true, AdapterRuleLoadCode.RemoteAccepted);
        }
        catch (Exception exception) when (exception is JsonException or AdapterRuleValidationException)
        {
            return Fallback(AdapterRuleLoadCode.PayloadInvalid);
        }
    }

    private static AdapterRuleLoadResult Fallback(AdapterRuleLoadCode code) =>
        new(ChatGptAdapterRules.BuiltIn, false, code);

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > maximumBytes)
            {
                throw new AdapterRuleDocumentTooLargeException();
            }

            buffer.Write(chunk, 0, read);
        }
    }

    private sealed class AdapterRuleDocumentTooLargeException : Exception;
}
