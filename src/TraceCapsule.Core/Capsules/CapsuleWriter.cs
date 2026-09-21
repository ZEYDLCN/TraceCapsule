using System.IO.Compression;
using System.Text.Json;
using TraceCapsule.Core.Model;

namespace TraceCapsule.Core.Capsules;

/// <summary>Writes a <see cref="Capsule"/> to the on-disk ZIP layout described in the
/// README: <c>metadata.json</c>, <c>request.json</c>, <c>response.json</c>,
/// <c>trace.json</c>, <c>external-http.json</c>, <c>events.json</c>,
/// <c>exceptions.json</c>, <c>timing.json</c>, <c>determinism.json</c>.</summary>
public static class CapsuleWriter
{
    public static async Task WriteAsync(Capsule capsule, Stream output, CancellationToken cancellationToken = default)
    {
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        await WriteEntryAsync(archive, "metadata.json", capsule.Metadata, cancellationToken);
        if (capsule.Request is not null) await WriteEntryAsync(archive, "request.json", capsule.Request, cancellationToken);
        if (capsule.Response is not null) await WriteEntryAsync(archive, "response.json", capsule.Response, cancellationToken);
        if (capsule.Trace.Count > 0) await WriteEntryAsync(archive, "trace.json", capsule.Trace, cancellationToken);
        if (capsule.ExternalHttpCalls.Count > 0) await WriteEntryAsync(archive, "external-http.json", capsule.ExternalHttpCalls, cancellationToken);
        if (capsule.Events.Count > 0) await WriteEntryAsync(archive, "events.json", capsule.Events, cancellationToken);
        if (capsule.Exceptions.Count > 0) await WriteEntryAsync(archive, "exceptions.json", capsule.Exceptions, cancellationToken);
        if (capsule.Timing is not null) await WriteEntryAsync(archive, "timing.json", capsule.Timing, cancellationToken);
        if (capsule.Determinism.Count > 0) await WriteEntryAsync(archive, "determinism.json", capsule.Determinism, cancellationToken);
    }

    public static async Task WriteAsync(Capsule capsule, string filePath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await using var file = File.Create(filePath);
        await WriteAsync(capsule, file, cancellationToken);
    }

    private static async Task WriteEntryAsync<T>(ZipArchive archive, string entryName, T value, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await JsonSerializer.SerializeAsync(entryStream, value, CapsuleJson.Options, cancellationToken);
    }
}
