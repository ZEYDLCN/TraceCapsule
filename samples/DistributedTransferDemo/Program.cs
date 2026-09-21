using DistributedTransferDemo;

var capsuleDir = Path.Combine(AppContext.BaseDirectory, "capsules");
await using var handles = await DemoHost.StartAsync(
    transferUrl: "http://localhost:5401",
    fraudUrl: "http://localhost:5402",
    paymentUrl: "http://localhost:5403",
    capsuleDir: capsuleDir);

Console.WriteLine("TraceCapsule distributed transfer demo");
Console.WriteLine($"  Transfer API:  http://localhost:5401/api/transfers");
Console.WriteLine($"  Fraud API:     http://localhost:5402 (internal)");
Console.WriteLine($"  Payment API:   http://localhost:5403 (internal)");
Console.WriteLine($"  Capsules dir:  {capsuleDir}");
Console.WriteLine();
Console.WriteLine("Try a normal transfer:");
Console.WriteLine("""  curl -X POST http://localhost:5401/api/transfers -H "Content-Type: application/json" -d "{\"fromAccount\":\"ACC-102\",\"toAccount\":\"ACC-550\",\"amount\":100}" """);
Console.WriteLine();
Console.WriteLine("Trigger the intentional Payment API timeout bug (amount >= 5000):");
Console.WriteLine("""  curl -X POST http://localhost:5401/api/transfers -H "Content-Type: application/json" -d "{\"fromAccount\":\"ACC-102\",\"toAccount\":\"ACC-550\",\"amount\":5000}" """);
Console.WriteLine();
Console.WriteLine("Then combine the three services' partial capsules:");
Console.WriteLine($"  tracecapsule merge <session-id> --from {capsuleDir}");
Console.WriteLine("(the session id is on each capsule's response — or just inspect any file in the capsules dir)");
Console.WriteLine();
Console.WriteLine("Press Enter to stop all three services.");
Console.ReadLine();
