using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenGameMate.Configuration;

public interface IAppSettingsStore
{
    Task<OpenGameMateSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(OpenGameMateSettings settings, CancellationToken cancellationToken = default);
}

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    public const long MaximumSettingsBytes = 64 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _settingsFile;

    public JsonAppSettingsStore(string settingsFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFile);
        _settingsFile = Path.GetFullPath(settingsFile);
    }

    public async Task<OpenGameMateSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_settingsFile))
            {
                return new OpenGameMateSettings();
            }

            var length = new FileInfo(_settingsFile).Length;
            if (length is <= 0 or > MaximumSettingsBytes)
            {
                throw new ConfigurationValidationException(
                    $"Settings file size must be between 1 and {MaximumSettingsBytes} bytes.");
            }

            await using var stream = new FileStream(
                _settingsFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            try
            {
                var settings = await JsonSerializer.DeserializeAsync<OpenGameMateSettings>(
                        stream,
                        SerializerOptions,
                        cancellationToken)
                    ?? throw new ConfigurationValidationException("Settings document cannot be null.");
                settings.Validate();
                return settings;
            }
            catch (JsonException exception)
            {
                throw new ConfigurationValidationException("Settings JSON is invalid or contains unknown fields.", exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        OpenGameMateSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        await _gate.WaitAsync(cancellationToken);
        var temporaryFile = _settingsFile + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(_settingsFile)
                ?? throw new ConfigurationValidationException("Settings file has no parent directory.");
            Directory.CreateDirectory(directory);

            await using (var stream = new FileStream(
                             temporaryFile,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (new FileInfo(temporaryFile).Length > MaximumSettingsBytes)
            {
                throw new ConfigurationValidationException("Serialized settings exceed the size limit.");
            }

            File.Move(temporaryFile, _settingsFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }

            _gate.Release();
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
