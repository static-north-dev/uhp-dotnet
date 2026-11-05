using StaticNorth.Uhp.ConsoleSample;

var baseUrl = Environment.GetEnvironmentVariable("UHP_BASE_URL") ?? throw new InvalidOperationException("Set UHP_BASE_URL to the server deployment root.");
var token = Environment.GetEnvironmentVariable("UHP_TOKEN") ?? throw new InvalidOperationException("Set UHP_TOKEN.");
var harnessId = Environment.GetEnvironmentVariable("UHP_HARNESS_ID") ?? throw new InvalidOperationException("Set UHP_HARNESS_ID to a configured harness.");
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
{
    BaseAddress = new Uri(baseUrl),
    Timeout = Timeout.InfiniteTimeSpan
};
await Scenario.RunAsync(http, token, harnessId, Environment.GetEnvironmentVariable("UHP_PROTOCOL_VERSION"), Console.WriteLine, cancellation.Token);
