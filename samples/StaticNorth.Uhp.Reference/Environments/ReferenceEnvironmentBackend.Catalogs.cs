using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using StaticNorth.Uhp.Server;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference.Environments;

public sealed partial class ReferenceEnvironmentBackend
{
    private Task<Wire.EnvironmentRuntimes>? _runtimeCatalog;
    private string? _pythonMinor;

    public async Task<Wire.EnvironmentRuntimes> ListEnvironmentRuntimesAsync(UhpRequestContext context, CancellationToken ct)
    {
        Task<Wire.EnvironmentRuntimes> catalog;

        lock (_gate)
        {
            catalog = _runtimeCatalog ??= ReadRuntimesAsync(_stopping.Token);
        }

        return await catalog
            .WaitAsync(ct)
            .ConfigureAwait(false);
    }

    private async Task<Wire.EnvironmentRuntimes> ReadRuntimesAsync(CancellationToken ct)
    {
        var work = Path.Combine(Path.GetFullPath(root), "toolchain-probe");
        Directory.CreateDirectory(work);
        var home = work + "-sandbox";
        await ReferenceSandboxProcess
            .PrepareAsync(home, work, ct)
            .ConfigureAwait(false);
        var python = await ReferenceSandboxProcess
            .RunAsync(home, work, ReferenceSandboxProcess.FindExecutable("python3"), ["-c", "import sys; print(str(sys.version_info.major)+'.'+str(sys.version_info.minor))"], ct)
            .ConfigureAwait(false);

        if (python.ExitCode != 0)
        {
            throw ReferenceBackend.Failure(503, "environment_unavailable", "The Python toolchain could not be inspected.");
        }

        _pythonMinor = python.Output.Trim();
        var node = Array.Empty<string>();

        try
        {
            var result = await ReferenceSandboxProcess
                .RunAsync(home, work, ReferenceSandboxProcess.FindExecutable("node"), ["--version"], ct)
                .ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                node = [result.Output
                    .Trim()
                    .TrimStart('v')
                    .Split('.')[0]];
            }
        }
        catch (FileNotFoundException)
        {
        }

        return new() { Python = [_pythonMinor], Node = node, Apt = false, Os = new() { Name = RuntimeInformation.OSDescription, Version = Environment.OSVersion.VersionString } };
    }

    public async Task<Wire.EnvironmentPackageCheck> CheckEnvironmentPackageAsync(Wire.CheckEnvironmentPackageQuery query, UhpRequestContext context, CancellationToken ct)
    {
        var manager = query.Manager ?? "pip";
        var spec = query.Spec ?? "";

        if (manager is not ("pip" or "npm"))
        {
            throw ReferenceBackend.Failure(400, "environment_invalid", "The local toolchain supports pip and npm registry checks.");
        }

        var end = spec
            .AsSpan()
            .IndexOfAny("<>=!~[; ".AsSpan());
        var name = end < 0 ? spec : spec[..end];
        string? pin = null;

        if (manager == "npm")
        {
            var separator = name.LastIndexOf('@');

            if (separator > 0)
            {
                pin = name[(separator + 1)..];
                name = name[..separator];
            }
        }
        else if (spec.IndexOf("==", StringComparison.Ordinal) is var separator && separator >= 0)
        {
            pin = spec[(separator + 2)..];
        }

        if (string.IsNullOrWhiteSpace(name) || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.' or '@' or '/')))
        {
            throw ReferenceBackend.Failure(400, "environment_invalid", "A package name is required.");
        }

        var answer = new Wire.EnvironmentPackageCheck { Manager = manager, Name = name, Spec = spec, Exists = null };
        var url = manager == "pip" ? "https://pypi.org/pypi/" + Uri.EscapeDataString(name) + "/json" : "https://registry.npmjs.org/" + Uri.EscapeDataString(name);

        try
        {
            using var response = await _registries
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return answer with { Exists = false };
            }

            if (!response.IsSuccessStatusCode)
            {
                return answer with { Error = "The package registry did not answer successfully." };
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(ct)
                .ConfigureAwait(false);
            var bytes = await ReadMemberAsync(stream, 8_388_608, ct)
                .ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);
            var body = document.RootElement;
            var latest = manager == "pip" ? body
                .GetProperty("info")
                .GetProperty("version")
                .GetString() : body
                    .GetProperty("dist-tags")
                    .GetProperty("latest")
                    .GetString();
            var versions = body.GetProperty(manager == "pip" ? "releases" : "versions");
            return answer with { Exists = true, Latest = latest, Version = pin is not null && versions.TryGetProperty(pin, out _) ? pin : null };
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException || exception is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return answer with { Error = "The package registry could not be asked." };
        }
    }
}
