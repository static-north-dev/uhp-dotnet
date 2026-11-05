using System.Globalization;
using System.Text;
using System.Text.Json;
using StaticNorth.Uhp.Server;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference.Environments;

public sealed partial class ReferenceEnvironmentBackend
{
    public Task<Wire.EnvironmentBuild> BuildEnvironmentAsync(string environmentId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = Find(environmentId, Owner(context));

            if (node.Building)
            {
                throw ReferenceBackend.Failure(409, "environment_busy", "Another build is running.");
            }

            var number = node.Versions.Count + 1L;
            var version = new ReferenceEnvironmentVersion(Path.Combine(Path.GetFullPath(root), "versions", environmentId, number.ToString(CultureInfo.InvariantCulture)), new()
            {
                Id = environmentId,
                Version = number,
                Status = "building",
                Stage = "copying files",
                StartedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Log = "",
                Packages = []
            });
            var files = node.Files.ToArray();
            node.Building = true;
            node.Versions.Add(number, version);
            node.Value = node.Value with { Status = "building", UpdatedAt = Now() };
            version.Completion = BuildAsync(node, version, node.Configuration, files, _stopping.Token);
            return Task.FromResult(version.Build);
        }
    }

    public Task<Wire.EnvironmentBuild> GetEnvironmentBuildAsync(string environmentId, string version, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = Find(environmentId, Owner(context));

            if (!long.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || !node.Versions.TryGetValue(number, out var build))
            {
                throw UhpServerException.NotFound("environment_not_found");
            }

            return Task.FromResult(build.Build);
        }
    }

    private async Task BuildAsync(ReferenceEnvironmentNode node, ReferenceEnvironmentVersion version, Wire.EnvironmentCreate configuration, KeyValuePair<string, byte[]>[] files, CancellationToken stopping)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var ct = timeout.Token;
        var work = version.Root + "-work";
        var home = version.Root + "-sandbox";
        var log = new StringBuilder();

        try
        {
            Directory.CreateDirectory(work);
            await ReferenceSandboxProcess.PrepareAsync(home, work, ct).ConfigureAwait(false);

            foreach (var file in files)
            {
                if (IsDependencyPath(file.Key))
                {
                    continue;
                }

                var path = Path.Combine(work, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await System.IO.File.WriteAllBytesAsync(path, file.Value, ct).ConfigureAwait(false);
            }

            var packages = new List<Wire.EnvironmentPackage>();
            var hasPython = System.IO.File.Exists(Path.Combine(work, "requirements.txt")) || System.IO.File.Exists(Path.Combine(work, "pyproject.toml")) || configuration.Packages?.Pip is { Length: > 0 };

            if (hasPython)
            {
                Stage(node, version, "python packages", log);
                var python = ReferenceSandboxProcess.FindExecutable("python3");
                await StepAsync(home, work, python, ["-m", "venv", "--copies", Path.Combine(work, ".venv")], log, ct).ConfigureAwait(false);
                python = Path.Combine(work, ".venv", "bin", "python");

                if (System.IO.File.Exists(Path.Combine(work, "requirements.txt")))
                {
                    await StepAsync(home, work, python, ["-m", "pip", "install", "--disable-pip-version-check", "--no-cache-dir", "-r", "requirements.txt"], log, ct).ConfigureAwait(false);
                    Stage(node, version, "python packages", log);
                }

                if (System.IO.File.Exists(Path.Combine(work, "pyproject.toml")))
                {
                    await StepAsync(home, work, python, ["-m", "pip", "install", "--disable-pip-version-check", "--no-cache-dir", "."], log, ct).ConfigureAwait(false);
                    Stage(node, version, "python packages", log);
                }

                foreach (var spec in configuration.Packages?.Pip ?? [])
                {
                    await StepAsync(home, work, python, ["-m", "pip", "install", "--disable-pip-version-check", "--no-cache-dir", "--", spec], log, ct).ConfigureAwait(false);
                    Stage(node, version, "python packages", log);
                }

                var installed = await ReferenceSandboxProcess.RunAsync(home, work, python, ["-m", "pip", "list", "--format=json", "--disable-pip-version-check"], ct).ConfigureAwait(false);

                if (installed.ExitCode != 0)
                {
                    throw new IOException("The Python package inventory could not be read.");
                }

                using var list = JsonDocument.Parse(installed.Output);

                foreach (var package in list.RootElement.EnumerateArray())
                {
                    packages.Add(new() { Manager = "pip", Name = package.GetProperty("name").GetString()!, Version = package.GetProperty("version").GetString() });
                }
            }

            if (System.IO.File.Exists(Path.Combine(work, "package.json")) || configuration.Packages?.Npm is { Length: > 0 })
            {
                Stage(node, version, "node packages", log);
                var npm = ReferenceSandboxProcess.FindExecutable("npm");

                if (System.IO.File.Exists(Path.Combine(work, "package.json")))
                {
                    await StepAsync(home, work, npm, [System.IO.File.Exists(Path.Combine(work, "package-lock.json")) ? "ci" : "install", "--no-audit", "--no-fund"], log, ct).ConfigureAwait(false);
                }

                foreach (var spec in configuration.Packages?.Npm ?? [])
                {
                    await StepAsync(home, work, npm, ["install", "--no-audit", "--no-fund", "--", spec], log, ct).ConfigureAwait(false);
                    Stage(node, version, "node packages", log);
                }

                var installed = await ReferenceSandboxProcess.RunAsync(home, work, npm, ["list", "--json", "--depth=0"], ct).ConfigureAwait(false);
                using var list = JsonDocument.Parse(installed.Output);

                if (list.RootElement.TryGetProperty("dependencies", out var dependencies))
                {
                    foreach (var package in dependencies.EnumerateObject())
                    {
                        packages.Add(new() { Manager = "npm", Name = package.Name, Version = package.Value.TryGetProperty("version", out var value) ? value.GetString() : null });
                    }
                }
            }

            if (configuration.Packages?.Apt is { Length: > 0 })
            {
                throw new IOException("The local reference toolchain does not provision apt packages; runtimes reports apt=false.");
            }

            if (System.IO.File.Exists(Path.Combine(work, "setup.sh")))
            {
                Stage(node, version, "setup.sh", log);
                await StepAsync(home, work, "/bin/sh", ["setup.sh"], log, ct).ConfigureAwait(false);
            }

            Stage(node, version, "finishing", log);
            await PublishSnapshotAsync(work, version.Root, ct).ConfigureAwait(false);

            lock (_gate)
            {
                version.Build = version.Build with { Status = "ready", Stage = null, FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Log = log.ToString(), Packages = packages.ToArray(), Files = files.Length, Bytes = files.Sum(file => (long)file.Value.Length) };
                node.Value = node.Value with { Status = "ready", Version = version.Build.Version, UpdatedAt = Now() };
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                version.Build = version.Build with { Status = "failed", Stage = null, Error = exception.Message, Log = log.ToString(), FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                node.Value = node.Value with { Status = node.Value.Version is null ? "failed" : "ready", UpdatedAt = Now() };
            }
        }
        finally
        {
            lock (_gate)
            {
                node.Building = false;
            }
        }
    }

    private void Stage(ReferenceEnvironmentNode node, ReferenceEnvironmentVersion version, string stage, StringBuilder log)
    {
        lock (_gate)
        {
            version.Build = version.Build with { Stage = stage, Log = log.ToString() };
        }
    }

    private static async Task StepAsync(string home, string cwd, string executable, string[] arguments, StringBuilder log, CancellationToken ct)
    {
        var result = await ReferenceSandboxProcess.RunAsync(home, cwd, executable, arguments, ct).ConfigureAwait(false);
        log.AppendLine(result.Output).AppendLine(result.Error);

        if (result.ExitCode != 0)
        {
            throw new IOException("The installer exited with status " + result.ExitCode.ToString(CultureInfo.InvariantCulture) + ". See the build log.");
        }
    }

    internal static async Task PublishSnapshotAsync(string work, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };

        foreach (var file in Directory.EnumerateFiles(work, "*", options))
        {
            var relative = Path.GetRelativePath(work, file);

            if (relative.StartsWith(".build-", StringComparison.Ordinal) || relative.Contains("/__pycache__/", StringComparison.Ordinal) || relative.EndsWith(".pyc", StringComparison.Ordinal))
            {
                continue;
            }

            var path = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = await ReferenceFileSystem.ReadAsync(work, relative, MaximumImportBytes, ct).ConfigureAwait(false);

            if (bytes is null)
            {
                throw new IOException("A build output is unsafe or exceeds the snapshot file limit.");
            }

            if (relative.StartsWith(".venv/bin/", StringComparison.Ordinal) && bytes.AsSpan().StartsWith("#!"u8))
            {
                bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(work, target, StringComparison.Ordinal));
            }

            await System.IO.File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);

            if (!OperatingSystem.IsWindows())
            {
                const UnixFileMode permissions = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
                System.IO.File.SetUnixFileMode(path, System.IO.File.GetUnixFileMode(file) & permissions);
            }
        }
    }

    private static bool IsDependencyPath(string path) => path.StartsWith(".venv/", StringComparison.Ordinal) || path.StartsWith("node_modules/", StringComparison.Ordinal) || path.StartsWith(".git/", StringComparison.Ordinal);

    private async Task<List<ReferenceEnvironmentMember>> ImportRepositoryAsync(string url, string? reference, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != "https" || address.UserInfo.Length != 0 || reference?.StartsWith('-') == true)
        {
            throw Invalid("Repository imports require an HTTPS URL and a valid ref.");
        }

        var work = Path.Combine(Path.GetFullPath(root), "imports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var home = work + "-sandbox";

        try
        {
            await ReferenceSandboxProcess.PrepareAsync(home, work, ct).ConfigureAwait(false);
            var git = ReferenceSandboxProcess.FindExecutable("git");
            var log = new StringBuilder();
            await StepAsync(home, work, git, ["clone", "--no-checkout", "--depth=1", "--", url, "repository"], log, ct).ConfigureAwait(false);

            if (reference is not null)
            {
                await StepAsync(home, work, git, ["-C", "repository", "fetch", "--depth=1", "origin", reference], log, ct).ConfigureAwait(false);
            }

            var archive = Path.Combine(work, "source.tar");
            await StepAsync(home, work, git, ["-C", "repository", "archive", "--format=tar", "--output=" + archive, reference is null ? "HEAD" : "FETCH_HEAD"], log, ct).ConfigureAwait(false);
            var bytes = await System.IO.File.ReadAllBytesAsync(archive, ct).ConfigureAwait(false);
            return (await ReadArchiveAsync(bytes, ct).ConfigureAwait(false)).Members;
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }

            if (Directory.Exists(home))
            {
                Directory.Delete(home, true);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        Task[] builds;

        lock (_gate)
        {
            builds = _retained.SelectMany(node => node.Versions.Values).Select(version => version.Completion).ToArray();
        }

        await Task.WhenAll(builds).ConfigureAwait(false);
        _registries.Dispose();
        _stopping.Dispose();
    }
}
