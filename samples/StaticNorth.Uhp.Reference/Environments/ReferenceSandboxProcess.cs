using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference.Environments;

internal static class ReferenceSandboxProcess
{
    public static async Task PrepareAsync(string home, string writableRoot, CancellationToken ct)
    {
        Directory.CreateDirectory(home);
        var config = new StringBuilder("default_permissions = \"uhp-build\"\napproval_policy = \"never\"\n[features]\nnetwork_proxy = true\n[permissions.uhp-build.filesystem]\n\":minimal\" = \"read\"\n");
        PathRule(config, CodexConnection.Executable, "read");
        PathRule(config, writableRoot, "write");

        if (OperatingSystem.IsMacOS() && Directory.Exists("/opt/homebrew"))
        {
            PathRule(config, "/opt/homebrew", "read");
        }
        else if (Directory.Exists("/usr/local"))
        {
            PathRule(config, "/usr/local", "read");
        }

        ReferenceNetworkPolicy.WriteToml(config, "uhp-build");
        await System.IO.File
            .WriteAllTextAsync(Path.Combine(home, "config.toml"), config.ToString(), ct)
            .ConfigureAwait(false);
    }

    public static async Task<ReferenceProcessResult> RunAsync(string home, string cwd, string executable, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo(CodexConnection.Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = cwd
        };
        start.ArgumentList.Add("sandbox"); start.ArgumentList.Add("--"); start.ArgumentList.Add(executable);

        for (var index = 0; index < arguments.Count; index++)
        {
            start.ArgumentList.Add(arguments[index]);
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";
        start.Environment.Clear();
        start.Environment["PATH"] = path;
        start.Environment["HOME"] = Path.Combine(cwd, ".build-home");
        start.Environment["CODEX_HOME"] = home;
        start.Environment["TMPDIR"] = Path.Combine(cwd, ".build-tmp");
        start.Environment["LANG"] = "C.UTF-8";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_ALLOW_PROTOCOL"] = "https";
        Directory.CreateDirectory(start.Environment["HOME"]!);
        Directory.CreateDirectory(start.Environment["TMPDIR"]!);
        using var process = Process.Start(start) ?? throw new IOException("The sandbox process could not start.");
        var output = ReadBoundedAsync(process.StandardOutput, ct);
        var error = ReadBoundedAsync(process.StandardError, ct);

        try
        {
            await process
                .WaitForExitAsync(ct)
                .ConfigureAwait(false);
            await Task
                .WhenAll(output, error)
                .ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                await process
                    .WaitForExitAsync()
                    .ConfigureAwait(false);
            }

            // Observe both pipe readers even on cancellation.
            try
            {
                await Task
                    .WhenAll(output, error)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
        }
    }

    public static string FindExecutable(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var file = new FileInfo(Path.Combine(directory, name));

            if (file.Exists)
            {
                return file.ResolveLinkTarget(true)?.FullName ?? file.FullName;
            }
        }

        throw new FileNotFoundException("A required toolchain executable is not installed: " + name);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var result = new StringBuilder();
        var buffer = System.Buffers.ArrayPool<char>.Shared.Rent(4096);

        try
        {
            int read;

            while ((read = await reader
                .ReadAsync(buffer.AsMemory(), ct)
                .ConfigureAwait(false)) > 0)
            {
                var retained = Math.Min(read, 1_048_576 - result.Length);

                if (retained > 0)
                {
                    result.Append(buffer, 0, retained);
                }
            }

            return result.ToString();
        }
        finally
        {
            System.Buffers.ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static void PathRule(StringBuilder text, string path, string access) => text
        .Append(JsonSerializer.Serialize(path, Wire.UhpContractJsonContext.Default.String))
        .Append(" = \"")
        .Append(access)
        .AppendLine("\"");
}
