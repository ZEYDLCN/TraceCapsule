using TraceCapsule.Cli.Compare;
using TraceCapsule.Core.Analysis;
using TraceCapsule.Core.Model;

namespace TraceCapsule.Cli;

internal static class CapsuleFormatting
{
    public static void PrintInspectSummary(Capsule capsule, IncidentAnalysis analysis)
    {
        Console.WriteLine("TraceCapsule");
        Console.WriteLine($"Trace:              {capsule.Metadata.TraceId}");
        Console.WriteLine($"Request:            {capsule.Metadata.Request.Method} {capsule.Metadata.Request.Path}");
        if (capsule.Timing is not null) Console.WriteLine($"Duration:           {capsule.Timing.TotalDurationMs:F0} ms");
        if (capsule.Metadata.Services.Count > 0) Console.WriteLine($"Services:           {string.Join(", ", capsule.Metadata.Services)}");
        if (capsule.Response is not null) Console.WriteLine($"Status:             {capsule.Response.StatusCode}");
        Console.WriteLine($"Spans:              {capsule.Trace.Count}");
        Console.WriteLine($"External calls:     {capsule.ExternalHttpCalls.Count}");
        Console.WriteLine($"Queue events:       {capsule.Events.Count}");
        Console.WriteLine($"Exceptions:         {capsule.Exceptions.Count}");

        var exception = capsule.Exceptions.FirstOrDefault();
        if (exception is not null)
        {
            var span = capsule.Trace.FirstOrDefault(s => s.SpanId == exception.SpanId);
            if (span is not null) Console.WriteLine($"Failed span:        {span.ServiceName}.{span.Name}");
            Console.WriteLine($"Exception:          {exception.Type}: {exception.Message}");
        }

        var worstCall = capsule.ExternalHttpCalls.OrderByDescending(c => c.DurationMs).FirstOrDefault();
        if (worstCall is not null)
        {
            Console.WriteLine($"External API:       {worstCall.DependencyName}");
            Console.WriteLine($"Recorded response:  HTTP {worstCall.ResponseStatusCode} ({worstCall.DurationMs:F0} ms)");
        }

        if (analysis.Findings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Analysis");
            foreach (var finding in analysis.Findings)
            {
                Console.WriteLine($"  {finding.Title}: {finding.Detail}");
            }
        }
    }

    public static void PrintReplaySummary(Capsule original, Capsule result, string outputPath)
    {
        Console.WriteLine("TraceCapsule replay");
        Console.WriteLine($"Trace:              {original.Metadata.TraceId}");
        Console.WriteLine($"Production status:  {original.Response?.StatusCode.ToString() ?? "(unknown)"}");
        Console.WriteLine($"Replay status:      {result.Response?.StatusCode.ToString() ?? "(no response)"}");
        Console.WriteLine($"Replay duration:    {result.Timing?.TotalDurationMs:F0} ms");
        if (result.Exceptions.Count > 0) Console.WriteLine($"Replay exception:   {result.Exceptions[0].Type}: {result.Exceptions[0].Message}");
        Console.WriteLine($"Result written to:  {outputPath}");
    }

    public static void PrintCompareReport(ComparisonReport report)
    {
        Console.WriteLine("Production vs Replay");
        Console.WriteLine();
        foreach (var line in report.Lines)
        {
            Console.WriteLine(line.Label);
            Console.WriteLine($"  Production: {line.ProductionValue}");
            Console.WriteLine($"  Replay:     {line.ReplayValue}");
            Console.WriteLine($"  {(line.IsMatch ? "MATCH" : "MISMATCH")}");
            Console.WriteLine();
        }
        Console.WriteLine(report.AllMatch ? "All compared fields match." : "Some fields differ — see MISMATCH lines above.");
    }
}
