using System.Text.Json;
using StaticNorth.Uhp.ConsoleSample;
using SkipTestException = TUnit.Core.Exceptions.SkipTestException;

namespace StaticNorth.Uhp.Client.InteropTests;

public sealed class InteropTests
{
    [Test]
    [Arguments("HARNESSROUTER")]
    [Arguments("SUPERQODE")]
    public async Task SamePublicConsumerWorksAgainstIndependentServers(string implementation)
    {
        var prefix = "UHP_" + implementation + "_";
        var url = Environment.GetEnvironmentVariable(prefix + "BASE_URL");
        var token = Environment.GetEnvironmentVariable(prefix + "TOKEN");
        var harnessId = Environment.GetEnvironmentVariable(prefix + "HARNESS_ID");
        var serverVersion = Environment.GetEnvironmentVariable(prefix + "SERVER_VERSION");
        var version = Environment.GetEnvironmentVariable(prefix + "PROTOCOL_VERSION");
        var results = new Dictionary<string, string>
        {
            ["discovery"] = "skip",
            ["list-harnesses"] = "skip",
            ["get-harness"] = "skip",
            ["models"] = "skip",
            ["harness-models"] = "skip",
            ["run"] = "skip",
            ["stream"] = "skip",
            ["readback"] = "skip",
            ["continuation"] = "skip",
            ["cancel-response"] = "skip",
            ["cancel-session"] = "skip"
        };
        UhpClient? client = null;
        string? reason = null;
        try
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(harnessId) || string.IsNullOrWhiteSpace(serverVersion))
            {
                reason = "A pinned server URL, credential, configured harness ID, and server version are required; this combination was not verified.";
                Skip.Test(reason);
            }

            using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(url),
                Timeout = Timeout.InfiniteTimeSpan
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            client = await UhpClient.ConnectAsync(http, new UhpClientOptions { ProtocolVersion = version, GetBearerTokenAsync = _ => ValueTask.FromResult<string>(token) }, timeout.Token);
            if (client.Discovery.ImplementationVersion is { } advertised && advertised != serverVersion)
            {
                reason = "The advertised server version differs from the configured pin.";
                Assert.Fail(reason);
            }

            results["discovery"] = "pass";
            await Scenario.RunAsync(http, token, harnessId, version, message =>
            {
                var operation = message.Split(':', 2)[0];
                if (results.ContainsKey(operation))
                {
                    results[operation] = "pass";
                }
            }, timeout.Token);
        }
        catch (SkipTestException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var pending = results.FirstOrDefault(pair => pair.Value == "skip");
            if (pending.Key is not null)
            {
                results[pending.Key] = "fail";
            }

            reason ??= exception is UhpApiException api ? $"HTTP {(int?)api.StatusCode}: {api.Code}" : exception.GetType().Name;
            Assert.Fail("Interoperability failed: " + reason);
        }
        finally
        {
            var folder = Environment.GetEnvironmentVariable("UHP_INTEROP_RESULTS") ?? Path.Combine(AppContext.BaseDirectory, "interop-results");
            Directory.CreateDirectory(folder);
            var report = new InteropReport(implementation, serverVersion, client?.Discovery.ImplementationName, client?.Discovery.ImplementationVersion, client?.ProtocolVersion, client?.Discovery.Capabilities.Values, DateTimeOffset.UtcNow, results, reason);
            await File.WriteAllTextAsync(Path.Combine(folder, implementation.ToLowerInvariant() + ".json"), JsonSerializer.Serialize(report, InteropJsonContext.Default.InteropReport));
        }
    }
}
