using System.Text.Json;
using OpenGameMate.Core;

namespace OpenGameMate.Diagnostics;

public sealed record Phase0EvidenceEvent(
    DateTimeOffset Timestamp,
    string CheckId,
    ValidationStatus Status,
    string Detail);

public sealed class Phase0EvidenceRecorder
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;

    public Phase0EvidenceRecorder(string filePath)
    {
        _filePath = filePath;
    }

    public async Task AppendAsync(string checkId, ValidationStatus status, string safeDetail)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Evidence file has no parent directory.");
        Directory.CreateDirectory(directory);

        var entry = new Phase0EvidenceEvent(DateTimeOffset.Now, checkId, status, safeDetail);
        var line = JsonSerializer.Serialize(entry) + Environment.NewLine;

        await _gate.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_filePath, line);
        }
        finally
        {
            _gate.Release();
        }
    }
}
