using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using StaticNorth.Uhp.Server;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference.Environments;

public sealed partial class ReferenceEnvironmentBackend(string root) : IUhpEnvironmentBackend, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ReferenceEnvironmentNode> _environments = new(StringComparer.Ordinal);
    private readonly List<ReferenceEnvironmentNode> _retained = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly HttpClient _registries = new() { Timeout = TimeSpan.FromSeconds(30) };
    private IUhpServerBackend? _harnesses;

    public void ConnectHarnesses(IUhpServerBackend backend) => _harnesses = backend;

    public void ValidateReference(string environmentId, string owner)
    {
        lock (_gate)
        {
            Find(environmentId, owner);
        }
    }

    public ReferenceEnvironmentSelection Select(string environmentId, string owner)
    {
        lock (_gate)
        {
            var node = Find(environmentId, owner);

            if (node.Value.Version is not { } active || !node.Versions.TryGetValue(active, out var version) || version.Build.Status != "ready")
            {
                throw ReferenceBackend.Failure(409, "environment_not_ready", "This environment has no active built version.");
            }

            return new(environmentId, active, version.Root, node.Value.Mount, node.Value.Entry, node);
        }
    }

    public Task<Wire.EnvironmentList> ListEnvironmentsAsync(UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(new Wire.EnvironmentList { Environments = _environments.Values.Where(node => node.Owner == Owner(context)).Select(View).ToArray() });
        }
    }

    public Task<Wire.Environment> CreateEnvironmentAsync(Wire.EnvironmentCreate request, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var slug = Slug(request.Name);

        lock (_gate)
        {
            var owner = Owner(context);

            if (_environments.Values.Any(node => node.Owner == owner && node.Value.Slug == slug))
            {
                throw ReferenceBackend.Failure(409, "environment_exists", "The environment's mount name is already in use.");
            }

            var id = $"henv_{Guid.NewGuid():N}";
            var value = new Wire.Environment
            {
                Id = id,
                Name = request.Name,
                Slug = slug,
                Description = request.Description,
                Entry = request.Entry,
                Mount = Path.Combine(Path.GetFullPath(root), "mounts", OwnerFolder(owner), id, slug),
                Status = "empty",
                CreatedAt = Now(),
                UpdatedAt = Now()
            };
            var node = new ReferenceEnvironmentNode(owner, value, request);
            _environments.Add(id, node);
            _retained.Add(node);
            return Task.FromResult(View(node));
        }
    }

    public Task<Wire.Environment> GetEnvironmentAsync(string environmentId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(View(Find(environmentId, Owner(context))));
        }
    }

    public Task<Wire.Environment> UpdateEnvironmentAsync(string environmentId, Wire.EnvironmentCreate request, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = Find(environmentId, Owner(context));
            node.Configuration = request;
            node.Value = node.Value with { Name = request.Name, Description = request.Description, Entry = request.Entry, UpdatedAt = Now() };
            return Task.FromResult(View(node));
        }
    }

    public async Task<Wire.DeleteEnvironmentResponse> DeleteEnvironmentAsync(string environmentId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ReferenceEnvironmentNode node;

        lock (_gate)
        {
            node = Find(environmentId, Owner(context));

            if (node.Building)
            {
                throw ReferenceBackend.Failure(409, "environment_busy", "A build is running.");
            }

            node.Deleting = true;
        }

        try
        {
            await node.ExecutionGate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
            {
                node.Deleting = false;
            }

            throw;
        }

        try
        {
            var versions = Path.Combine(Path.GetFullPath(root), "versions", environmentId);
            var mounts = Path.GetDirectoryName(node.Value.Mount)!;

            if (Directory.Exists(versions))
            {
                Directory.Delete(versions, true);
            }

            if (Directory.Exists(mounts))
            {
                Directory.Delete(mounts, true);
            }

            return new() { Id = environmentId, Deleted = true };
        }
        finally
        {
            lock (_gate)
            {
                _environments.Remove(environmentId);
                _retained.Remove(node);
            }

            node.ExecutionGate.Release();
        }
    }

    public Task<Wire.ListEnvironmentVersionsResponse> ListEnvironmentVersionsAsync(string environmentId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = Find(environmentId, Owner(context));
            return Task.FromResult(new Wire.ListEnvironmentVersionsResponse { Id = environmentId, Active = node.Value.Version, Versions = node.Versions.Values.Select(VersionView).ToArray() });
        }
    }

    public Task<Wire.Environment> ActivateEnvironmentVersionAsync(string environmentId, string version, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = Find(environmentId, Owner(context));

            if (!long.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || !node.Versions.TryGetValue(number, out var selected) || selected.Build.Status != "ready")
            {
                throw ReferenceBackend.Failure(409, "environment_not_ready", "The requested version has not finished building.");
            }

            node.Value = node.Value with { Version = number, Status = node.Building ? "building" : "ready", UpdatedAt = Now() };
            return Task.FromResult(View(node));
        }
    }

    public async Task<Wire.ListEnvironmentHarnessesResponse> ListEnvironmentHarnessesAsync(string environmentId, UhpRequestContext context, CancellationToken ct)
    {
        ValidateReference(environmentId, Owner(context));
        var harnesses = await (_harnesses ?? throw new InvalidOperationException("Harness catalog is not connected.")).ListHarnessesAsync(context, ct).ConfigureAwait(false);
        var result = new List<Wire.ListEnvironmentHarnessesResponseHarnessesItem>();

        for (var index = 0; index < harnesses.Count; index++)
        {
            var harness = harnesses[index];

            if (harness.Raw.ValueKind == JsonValueKind.Object && harness.Raw.TryGetProperty("environment", out var reference) && reference.GetString() == environmentId)
            {
                result.Add(new() { Id = harness.Id, Name = harness.Name, Base = harness.Base });
            }
        }

        return new() { Id = environmentId, Harnesses = result.ToArray() };
    }

    private ReferenceEnvironmentNode Find(string id, string owner) => _environments.TryGetValue(id, out var node) && node.Owner == owner && !node.Deleting ? node : throw UhpServerException.NotFound("environment_not_found");
    private static string Owner(UhpRequestContext context) => context.Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("A stable principal is required.");
    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static string OwnerFolder(string owner) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(owner)));
    private static Wire.Environment View(ReferenceEnvironmentNode node) => node.Value with
    {
        Files = new() { Count = node.Files.Count, Bytes = node.Files.Values.Sum(bytes => (long)bytes.Length) },
        Versions = node.Versions.Values.Select(VersionView).ToArray(),
        LatestVersion = node.Versions.Count == 0 ? null : node.Versions.Keys.Last(),
        Packages = node.Value.Version is { } active ? node.Versions[active].Build.Packages : [],
        Declared = new() { Pip = (node.Configuration.Packages?.Pip ?? []).Select(spec => new Wire.DeclaredPackage { Spec = spec }).ToArray() }
    };
    private static Wire.EnvironmentVersion VersionView(ReferenceEnvironmentVersion version) => new()
    {
        Version = version.Build.Version,
        Status = version.Build.Status,
        StartedAt = version.Build.StartedAt,
        FinishedAt = version.Build.FinishedAt,
        Error = version.Build.Error,
        Files = version.Build.Files,
        Bytes = version.Build.Bytes,
        Packages = version.Build.Packages?.Length
    };

    private static string Slug(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !char.IsLetterOrDigit(value[0]) || value.Length > 100)
        {
            throw ReferenceBackend.Failure(422, "environment_invalid", "The name must begin with a letter or digit.");
        }

        var slug = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            slug.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? char.ToLowerInvariant(character) : '-');
        }

        return slug.ToString();
    }
}
