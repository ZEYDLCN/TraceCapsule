using System.CommandLine;
using TraceCapsule.Cli;
using TraceCapsule.Cli.Compare;
using TraceCapsule.Cli.GenerateTest;
using TraceCapsule.Cli.Replay;
using TraceCapsule.Cli.Report;
using TraceCapsule.Core.Analysis;
using TraceCapsule.Core.Capsules;
using TraceCapsule.Core.Fault;

var pathArgument = new Argument<string>("path") { Description = "Path to a .capsule file" };
var htmlOption = new Option<string?>("--html") { Description = "Also write a self-contained, shareable HTML report to this path" };

var inspect = new Command("inspect", "Print a summary of a recorded capsule");
inspect.Add(pathArgument);
inspect.Add(htmlOption);
inspect.SetAction(async (parseResult, cancellationToken) =>
{
    var capsule = await CapsuleReader.ReadAsync(parseResult.GetValue(pathArgument)!, cancellationToken);
    var analysis = new HeuristicIncidentAnalyzer().Analyze(capsule);
    CapsuleFormatting.PrintInspectSummary(capsule, analysis);

    var htmlPath = parseResult.GetValue(htmlOption);
    if (htmlPath is not null)
    {
        await File.WriteAllTextAsync(htmlPath, HtmlReportBuilder.Build(capsule, analysis), cancellationToken);
        Console.WriteLine();
        Console.WriteLine($"HTML report written to: {htmlPath}");
    }
    return 0;
});

var targetUrlOption = new Option<string>("--target-url") { Required = true, Description = "Base URL of a running instance of the recorded service" };
var latencyOption = new Option<string[]>("--latency") { Description = "Inject extra latency into a named dependency on replay, e.g. payment-api=3000", AllowMultipleArgumentsPerToken = true };
var replayOutputOption = new Option<string?>("--output", "-o") { Description = "Where to write the replay result capsule (default: <name>.replay.capsule next to the input)" };

var replay = new Command("replay", "Replay a capsule's recorded request against a live target");
replay.Add(pathArgument);
replay.Add(targetUrlOption);
replay.Add(latencyOption);
replay.Add(replayOutputOption);
replay.SetAction(async (parseResult, cancellationToken) =>
{
    var path = parseResult.GetValue(pathArgument)!;
    var capsule = await CapsuleReader.ReadAsync(path, cancellationToken);
    var faults = FaultInjectionOptions.ParseLatencyArgs(parseResult.GetValue(latencyOption) ?? []);
    var targetUrl = new Uri(parseResult.GetValue(targetUrlOption)!);

    var result = await CapsuleReplayer.ReplayAsync(capsule, targetUrl, faults, cancellationToken);

    var outputPath = parseResult.GetValue(replayOutputOption)
        ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", $"{Path.GetFileNameWithoutExtension(path)}.replay.capsule");
    await CapsuleWriter.WriteAsync(result, outputPath, cancellationToken);
    CapsuleFormatting.PrintReplaySummary(capsule, result, outputPath);
    return 0;
});

var productionPathArgument = new Argument<string>("production") { Description = "The original (production) .capsule" };
var replayPathArgument = new Argument<string>("replay") { Description = "The replay result .capsule (from `tracecapsule replay`)" };
var ignoreBodyFieldOption = new Option<string[]>("--ignore-body-field")
{
    Description = "Exclude a JSON path from the response body diff, e.g. $.createdAt (supports * as a wildcard segment, e.g. $.items[*].updatedAt)",
    AllowMultipleArgumentsPerToken = true,
};

var compare = new Command("compare", "Diff a production capsule against a replay result");
compare.Add(productionPathArgument);
compare.Add(replayPathArgument);
compare.Add(ignoreBodyFieldOption);
compare.SetAction(async (parseResult, cancellationToken) =>
{
    var production = await CapsuleReader.ReadAsync(parseResult.GetValue(productionPathArgument)!, cancellationToken);
    var replayResult = await CapsuleReader.ReadAsync(parseResult.GetValue(replayPathArgument)!, cancellationToken);
    var ignoreBodyFields = parseResult.GetValue(ignoreBodyFieldOption) ?? [];
    var report = CapsuleComparer.Compare(production, replayResult, ignoreBodyFields);
    CapsuleFormatting.PrintCompareReport(report);
    return report.AllMatch ? 0 : 1;
});

var traceOption = new Option<string>("--trace") { Required = true, Description = "Trace id to look up" };
var fromOption = new Option<string>("--from") { Description = "Directory TraceCapsule writes .capsule files to", DefaultValueFactory = _ => "capsules" };
var exportToOption = new Option<string?>("--to", "-o") { Description = "Copy the capsule to this path instead of just reporting where it already is" };

var export = new Command("export", "Locate the capsule(s) TraceCapsule already wrote for a given trace id");
export.Add(traceOption);
export.Add(fromOption);
export.Add(exportToOption);
export.SetAction(async (parseResult, cancellationToken) =>
{
    var traceId = parseResult.GetValue(traceOption)!;
    var fromDir = parseResult.GetValue(fromOption)!;
    if (!Directory.Exists(fromDir))
    {
        Console.Error.WriteLine($"Directory '{fromDir}' does not exist.");
        return 1;
    }

    // File names carry a random per-write suffix so services sharing a trace id (Phase 5)
    // never clobber each other's capsule — so matching has to read Metadata.TraceId back
    // out of each candidate rather than assume a fixed file name.
    var matches = new List<string>();
    foreach (var file in Directory.EnumerateFiles(fromDir, "*.capsule"))
    {
        var capsule = await CapsuleReader.ReadAsync(file, cancellationToken);
        if (capsule.Metadata.TraceId == traceId) matches.Add(file);
    }

    if (matches.Count == 0)
    {
        Console.Error.WriteLine($"No capsule found for trace '{traceId}' in '{fromDir}'.");
        return 1;
    }
    if (matches.Count > 1)
    {
        Console.WriteLine($"{matches.Count} capsules share trace '{traceId}' (a distributed execution) — use `tracecapsule merge` to combine them, or pick one:");
        foreach (var match in matches) Console.WriteLine($"  {match}");
        return 0;
    }

    var found = matches[0];
    var to = parseResult.GetValue(exportToOption);
    if (to is not null)
    {
        File.Copy(found, to, overwrite: true);
        Console.WriteLine($"Capsule created: {to}");
    }
    else
    {
        Console.WriteLine($"Capsule created: {found}");
    }
    return 0;
});

var sessionArgument = new Argument<string>("session-id") { Description = "The shared X-TraceCapsule-Session id across the participating services' partial capsules" };
var mergeOutputOption = new Option<string?>("--output", "-o") { Description = "Where to write the merged capsule (default: <traceId>.merged.capsule in --from)" };

var merge = new Command("merge", "Combine per-service partial capsules from one distributed execution (Phase 5) into one");
merge.Add(sessionArgument);
merge.Add(fromOption);
merge.Add(mergeOutputOption);
merge.SetAction(async (parseResult, cancellationToken) =>
{
    var sessionId = parseResult.GetValue(sessionArgument)!;
    var fromDir = parseResult.GetValue(fromOption)!;
    var parts = new List<TraceCapsule.Core.Model.Capsule>();
    foreach (var file in Directory.EnumerateFiles(fromDir, "*.capsule"))
    {
        var candidate = await CapsuleReader.ReadAsync(file, cancellationToken);
        if (candidate.Metadata.SessionId == sessionId && !candidate.Metadata.IsMerged) parts.Add(candidate);
    }
    if (parts.Count == 0)
    {
        Console.Error.WriteLine($"No capsules with session id '{sessionId}' found in '{fromDir}'.");
        return 1;
    }
    var merged = CapsuleMerger.Merge(parts);
    merged.Metadata.IsMerged = true;
    var outputPath = parseResult.GetValue(mergeOutputOption) ?? Path.Combine(fromDir, $"{merged.Metadata.TraceId}.merged.capsule");
    await CapsuleWriter.WriteAsync(merged, outputPath, cancellationToken);
    Console.WriteLine($"Merged {parts.Count} capsule(s) from session '{sessionId}' into {outputPath}");
    return 0;
});

var bugPathArgument = new Argument<string>("bug") { Description = "The original (production) .capsule showing the failure" };
var fixedPathArgument = new Argument<string>("fixed") { Description = "A replay result .capsule (from `tracecapsule replay`) recorded after verifying the fix" };
var generateTestOutputOption = new Option<string>("--output", "-o") { Required = true, Description = "Where to write the generated xUnit test .cs file" };
var testNamespaceOption = new Option<string>("--namespace") { Description = "Namespace for the generated test class", DefaultValueFactory = _ => "TraceCapsule.GeneratedTests" };
var testClassNameOption = new Option<string?>("--class-name") { Description = "Name for the generated test class (default: derived from the trace id)" };
var targetUrlEnvOption = new Option<string>("--target-url-env") { Description = "Environment variable the generated test reads the target base URL from at run time", DefaultValueFactory = _ => "TRACECAPSULE_TARGET_URL" };

var generateTest = new Command("generate-test", "Generate a self-contained xUnit test proving a recorded bug no longer reproduces");
generateTest.Add(bugPathArgument);
generateTest.Add(fixedPathArgument);
generateTest.Add(generateTestOutputOption);
generateTest.Add(ignoreBodyFieldOption);
generateTest.Add(testNamespaceOption);
generateTest.Add(testClassNameOption);
generateTest.Add(targetUrlEnvOption);
generateTest.SetAction(async (parseResult, cancellationToken) =>
{
    var bug = await CapsuleReader.ReadAsync(parseResult.GetValue(bugPathArgument)!, cancellationToken);
    var fixedCapsule = await CapsuleReader.ReadAsync(parseResult.GetValue(fixedPathArgument)!, cancellationToken);
    var className = parseResult.GetValue(testClassNameOption)
        ?? $"Bug_{RegressionTestGenerator.SanitizeIdentifier(bug.Metadata.TraceId)}_RegressionTest";

    try
    {
        var source = RegressionTestGenerator.Generate(
            bug,
            fixedCapsule,
            parseResult.GetValue(testNamespaceOption)!,
            className,
            parseResult.GetValue(ignoreBodyFieldOption) ?? [],
            parseResult.GetValue(targetUrlEnvOption)!);
        var outputPath = parseResult.GetValue(generateTestOutputOption)!;
        await File.WriteAllTextAsync(outputPath, source, cancellationToken);
        Console.WriteLine($"Regression test written to: {outputPath}");
        return 0;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
});

var root = new RootCommand("TraceCapsule — turn production failures into replayable execution artifacts.");
root.Add(inspect);
root.Add(replay);
root.Add(compare);
root.Add(export);
root.Add(merge);
root.Add(generateTest);

return await root.Parse(args).InvokeAsync();
