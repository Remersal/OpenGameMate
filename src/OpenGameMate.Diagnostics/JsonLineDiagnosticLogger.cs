using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenGameMate.Diagnostics;

public interface IDiagnosticLogger
{
    Task AppendAsync(DiagnosticEvent diagnosticEvent, CancellationToken cancellationToken = default);
}

public sealed class JsonLineDiagnosticLogger : IDiagnosticLogger
{
    public const int MaximumEventBytes = 8 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _logsDirectory;

    public JsonLineDiagnosticLogger(string logsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        _logsDirectory = Path.GetFullPath(logsDirectory);
    }

    public async Task AppendAsync(
        DiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        diagnosticEvent.Validate();

        var line = JsonSerializer.Serialize(diagnosticEvent, SerializerOptions) + Environment.NewLine;
        if (Utf8WithoutBom.GetByteCount(line) > MaximumEventBytes)
        {
            throw new DiagnosticEventValidationException("Serialized diagnostic event exceeds the size limit.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_logsDirectory);
            var fileName = $"opengamemate-{diagnosticEvent.TimestampUtc.UtcDateTime:yyyyMMdd}.jsonl";
            var filePath = Path.Combine(_logsDirectory, fileName);
            await File.AppendAllTextAsync(filePath, line, Utf8WithoutBom, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
