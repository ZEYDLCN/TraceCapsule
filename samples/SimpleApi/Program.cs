using TraceCapsule.AspNetCore;
using TraceCapsule.Core.Redaction;
using TraceCapsule.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Redaction policy loaded from a YAML file (the README's own example) instead of hardcoded
// in Program.cs — see redaction.yaml next to this file.
var redactionPolicyPath = Path.Combine(builder.Environment.ContentRootPath, "redaction.yaml");
var redactionPolicy = File.Exists(redactionPolicyPath) ? RedactionPolicyLoader.FromFile(redactionPolicyPath) : RedactionPolicy.Default();

builder.Services.AddTraceCapsule(options =>
{
    options.EnableHttpRecording = true;
    options.EnableOpenTelemetry = true;
    options.EnableRedaction = true;
    options.RedactionPolicy = redactionPolicy;
    options.OutputDirectory = Path.Combine(builder.Environment.ContentRootPath, "capsules");
    options.AppVersion = "SimpleApi/1.0";
    options.CapturePolicy.SamplingRate = 1.0; // demo: record everything, don't rely on random sampling
});
CapsuleActivityListener.Enable();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseTraceCapsule();
app.UseAuthorization();

app.MapControllers();

// Demo endpoints used by the integration tests and by `docs/demo.md` to show recording end
// to end without needing the full DistributedTransferDemo (Phase 5) wired up.
app.MapPost("/echo", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    return Results.Text(body, "application/json");
});

app.MapGet("/boom", IResult () => throw new InvalidOperationException("simulated failure for TraceCapsule demo purposes"));

app.Run();

public partial class Program;
