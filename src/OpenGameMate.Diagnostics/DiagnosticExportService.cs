using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace OpenGameMate.Diagnostics;

public static class DiagnosticExportService
{
    public static int Export(string logsDirectory, string destinationZip)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationZip);

        if (File.Exists(destinationZip))
        {
            File.Delete(destinationZip);
        }

        var files = Directory.Exists(logsDirectory)
            ? Directory.EnumerateFiles(logsDirectory, "opengamemate-*.jsonl", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        using var archive = ZipFile.Open(destinationZip, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            archive.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
        }

        return files.Length;
    }
}
