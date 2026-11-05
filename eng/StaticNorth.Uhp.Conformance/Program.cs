using System.Text.Json;
using StaticNorth.Uhp.Conformance;

try
{
    if (args.Length != 3 || args[0] is not ("verify-suite" or "validate"))
    {
        throw new InvalidOperationException("Usage: <verify-suite|validate> <repository-root> <upstream-checkout|report.json>");
    }

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    var ct = cancellation.Token;
    using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(args[1], "eng/conformance/manifest.json"), ct));
    using var inventory = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(args[1], "docs/conformance-checks.json"), ct));
    SuiteVerifier.VerifyInventory(manifest.RootElement, inventory.RootElement);

    if (args[0] == "verify-suite")
    {
        await SuiteVerifier.VerifyAsync(args[2], manifest.RootElement, inventory.RootElement, ct);
        Console.WriteLine("Pinned suite hashes, schemas and complete check inventory verified.");
    }
    else
    {
        using var report = JsonDocument.Parse(await File.ReadAllBytesAsync(args[2], ct));
        ReportValidator.Validate(report.RootElement, inventory.RootElement);
        Console.WriteLine("Full conformance report verified: complete coverage, no failures, errors or skips.");
    }

    return 0;
}
catch (Exception exception) when (exception is InvalidOperationException or JsonException or IOException or OperationCanceledException or KeyNotFoundException or FormatException)
{
    Console.Error.WriteLine("Conformance gate failed: " + exception.Message);
    return 1;
}
