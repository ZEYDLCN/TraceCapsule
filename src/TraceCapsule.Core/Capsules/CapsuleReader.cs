using System.IO.Compression;
using System.Text.Json;
using TraceCapsule.Core.Model;

namespace TraceCapsule.Core.Capsules;

/// <summary>Reads a <c>.capsule</c> ZIP back into a <see cref="Capsule"/>. Missing entries
/// (e.g. no external calls were recorded) simply leave the corresponding collection empty —
/// every section is optional except <c>metadata.json</c>.</summary>
public static class CapsuleReader
{
    public static async Task<Capsule> ReadAsync(Stream input, CancellationToken cancellationToken = default)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var capsule = new Capsule
        {
            Metadata = await ReadEntryAsync<CapsuleMetadata>(archive, "metadata.json", cancellationToken)
                ?? throw new InvalidDataException("The capsule is missing required metadata.json."),
            Request = await ReadEntryAsync<HttpRequestRecord>(archive, "request.json", cancellationToken),
            Response = await ReadEntryAsync<HttpResponseRecord>(archive, "response.json", cancellationToken),
            Trace = await ReadEntryAsync<List<SpanRecord>>(archive, "trace.json", cancellationToken) ?? [],
            ExternalHttpCalls = await ReadEntryAsync<List<ExternalHttpCallRecord>>(archive, "external-http.json", cancellationToken) ?? [],
            Events = await ReadEntryAsync<List<QueueEventRecord>>(archive, "events.json", cancellationToken) ?? [],
            Exceptions = await ReadEntryAsync<List<ExceptionRecord>>(archive, "exceptions.json", cancellationToken) ?? [],
            Timing = await ReadEntryAsync<TimingRecord>(archive, "timing.json", cancellationToken),
            Determinism = await ReadEntryAsync<List<DeterminismEventRecord>>(archive, "determinism.json", cancellationToken) ?? [],
        };
        return capsule;
    }

    public static async Task<Capsule> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(filePath);
        return await ReadAsync(file, cancellationToken);
    }

    private static async Task<T?> ReadEntryAsync<T>(ZipArchive archive, string entryName, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null) return default;
        await using var entryStream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(entryStream, CapsuleJson.Options, cancellationToken);
    }
}
