using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using StaticNorth.Uhp.Server;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference.Environments;

public sealed partial class ReferenceEnvironmentBackend
{
    private const long MaximumImportBytes = 67_108_864;

    public Task<Wire.EnvironmentFileList> ListEnvironmentFilesAsync(string environmentId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = Find(environmentId, Owner(context));
            var entries = new List<Wire.EnvironmentFileEntry>();

            foreach (var directory in node.Directories)
            {
                entries.Add(new() { Path = directory, Dir = true });
            }

            foreach (var file in node.Files)
            {
                entries.Add(new() { Path = file.Key, Dir = false, Bytes = file.Value.Length });
            }

            return Task.FromResult(new Wire.EnvironmentFileList
            {
                Entries = entries
                    .OrderBy(entry => entry.Path, StringComparer.Ordinal)
                    .ToArray(),
                Count = node.Files.Count,
                Bytes = node.Files.Values.Sum(value => (long)value.Length)
            });
        }
    }

    public Task<UhpBinaryContent> GetEnvironmentFileAsync(string environmentId, string path, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        path = SourcePath(path);

        lock (_gate)
        {
            var node = Find(environmentId, Owner(context));

            if (!node.Files.TryGetValue(path, out var bytes))
            {
                throw UhpServerException.NotFound("file_not_found");
            }

            return Task.FromResult(new UhpBinaryContent(new MemoryStream(bytes, false), "application/octet-stream", Path.GetFileName(path)));
        }
    }

    public async Task<Wire.PutEnvironmentFileResponse> PutEnvironmentFileAsync(string environmentId, string path, UhpRequestBody request, UhpRequestContext context, CancellationToken ct)
    {
        path = SourcePath(path);
        ValidateReference(environmentId, Owner(context));
        var bytes = await ReadMemberAsync(request.Content, MaximumImportBytes, ct)
            .ConfigureAwait(false);

        lock (_gate)
        {
            var node = Find(environmentId, Owner(context));
            StoreMember(node, new(path, bytes));
            node.Value = node.Value with { UpdatedAt = Now() };
            return new() { Path = path, Bytes = bytes.Length, Dir = false };
        }
    }

    public Task<Wire.MakeEnvironmentDirectoryResponse> MakeEnvironmentDirectoryAsync(string environmentId, Wire.MakeEnvironmentDirectoryRequest request, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = SourcePath(request.Path);

        lock (_gate)
        {
            StoreMember(Find(environmentId, Owner(context)), new(path, null));
            return Task.FromResult(new Wire.MakeEnvironmentDirectoryResponse { Path = path, Dir = true });
        }
    }

    public Task<Wire.DeleteEnvironmentFileResponse> DeleteEnvironmentFileAsync(string environmentId, string path, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        path = SourcePath(path);

        lock (_gate)
        {
            var node = Find(environmentId, Owner(context));
            var prefix = path + "/";
            var files = node.Files.Keys
                .Where(key => key == path || key.StartsWith(prefix, StringComparison.Ordinal))
                .ToArray();
            var directories = node.Directories
                .Where(key => key == path || key.StartsWith(prefix, StringComparison.Ordinal))
                .ToArray();

            foreach (var file in files)
            {
                node.Files.Remove(file);
            }

            foreach (var directory in directories)
            {
                node.Directories.Remove(directory);
            }

            return Task.FromResult(new Wire.DeleteEnvironmentFileResponse { Path = path, Deleted = true });
        }
    }

    public async Task<Wire.EnvironmentImportResult> ImportEnvironmentAsync(string environmentId, UhpRequestBody request, Wire.ImportEnvironmentQuery query, UhpRequestContext context, CancellationToken ct)
    {
        ValidateReference(environmentId, Owner(context));
        var input = await ReadMemberAsync(request.Content, MaximumImportBytes, ct)
            .ConfigureAwait(false);
        List<ReferenceEnvironmentMember> members;
        long skipped = 0;
        var replace = query.Replace == 1;

        if (request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(input);
            var body = document.RootElement;

            if (!body.TryGetProperty("git", out var git) || !git.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String)
            {
                throw Invalid("An import requires an archive or a git repository.");
            }

            replace |= body.TryGetProperty("replace", out var replaceField) && replaceField.ValueKind == JsonValueKind.True;
            var reference = git.TryGetProperty("ref", out var branch) ? branch.GetString() : null;
            members = await ImportRepositoryAsync(url.GetString()!, reference, ct)
                .ConfigureAwait(false);
        }
        else
        {
            (members, skipped) = await ReadArchiveAsync(input, ct)
                .ConfigureAwait(false);
        }

        var prefix = members.Count > 0 ? members[0].Path.Split('/')[0] : "";

        if (prefix.Length > 0 && members.All(member => member.Path == prefix && member.Content is null || member.Path.StartsWith(prefix + "/", StringComparison.Ordinal)))
        {
            members = members
                .Where(member => member.Path != prefix)
                .Select(member => member with { Path = member.Path[(prefix.Length + 1)..] })
                .ToList();
        }

        lock (_gate)
        {
            var node = Find(environmentId, Owner(context));

            // Preflight the merged tree before changing any retained source.
            var staged = new ReferenceEnvironmentNode(node.Owner, node.Value, node.Configuration);

            if (!replace)
            {
                foreach (var entry in node.Files)
                {
                    staged.Files.Add(entry.Key, entry.Value);
                }

                staged.Directories.UnionWith(node.Directories);
            }

            foreach (var member in members)
            {
                StoreMember(staged, member);
            }

            node.Files.Clear();
            node.Directories.Clear();

            foreach (var entry in staged.Files)
            {
                node.Files.Add(entry.Key, entry.Value);
            }

            node.Directories.UnionWith(staged.Directories);
            return new() { Written = members.Count(member => member.Content is not null), Skipped = skipped, Count = node.Files.Count, Bytes = node.Files.Values.Sum(value => (long)value.Length) };
        }
    }

    private static async Task<(List<ReferenceEnvironmentMember> Members, long Skipped)> ReadArchiveAsync(byte[] bytes, CancellationToken ct)
    {
        var members = new List<ReferenceEnvironmentMember>();
        long skipped = 0;
        long total = 0;
        using var source = new MemoryStream(bytes, false);

        try
        {
            if (bytes.Length >= 2 && bytes[0] == 'P' && bytes[1] == 'K')
            {
                await using var archive = new ZipArchive(source, ZipArchiveMode.Read);

                foreach (var entry in archive.Entries)
                {
                    var path = entry.FullName.TrimEnd('/');
                    var mode = (entry.ExternalAttributes >> 16) & 0xF000;

                    if (mode == 0xA000 || !IsSourcePath(path))
                    {
                        skipped++;
                        continue;
                    }

                    if (entry.FullName.EndsWith('/'))
                    {
                        members.Add(new(path, null));
                        continue;
                    }

                    await using var content = await entry
                        .OpenAsync(ct)
                        .ConfigureAwait(false);
                    var value = await ReadMemberAsync(content, MaximumImportBytes - total, ct)
                        .ConfigureAwait(false);
                    total += value.Length;
                    members.Add(new(path, value));
                }
            }
            else
            {
                using var compressed = bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B ? new GZipStream(source, CompressionMode.Decompress, true) : null;
                await using var archive = new TarReader(compressed is null ? source : compressed, true);

                while (await archive
                    .GetNextEntryAsync(false, ct)
                    .ConfigureAwait(false) is { } entry)
                {
                    var path = entry.Name.TrimEnd('/');

                    if (!IsSourcePath(path) || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory))
                    {
                        skipped++;
                        continue;
                    }

                    var value = entry.EntryType == TarEntryType.Directory ? null : await ReadMemberAsync(entry.DataStream ?? Stream.Null, MaximumImportBytes - total, ct)
                        .ConfigureAwait(false);
                    total += value?.Length ?? 0;
                    members.Add(new(path, value));
                }
            }
        }
        catch (InvalidDataException)
        {
            throw Invalid("The archive is invalid.");
        }

        return (members, skipped);
    }

    private static async Task<byte[]> ReadMemberAsync(Stream content, long maximum, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var scratch = System.Buffers.ArrayPool<byte>.Shared.Rent(16_384);

        try
        {
            int read;

            while ((read = await content
                .ReadAsync(scratch, ct)
                .ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maximum)
                {
                    throw ReferenceBackend.Failure(413, "file_too_large", "The import exceeds its expanded byte limit.", StaticNorth.Uhp.Core.UhpJson.CreateElement(static writer =>
                    {
                        writer.WriteStartObject(); writer.WriteNumber("max_bytes", MaximumImportBytes); writer.WriteEndObject();
                    }));
                }

                await buffer
                    .WriteAsync(scratch.AsMemory(0, read), ct)
                    .ConfigureAwait(false);
            }

            return buffer.ToArray();
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    private static void StoreMember(ReferenceEnvironmentNode node, ReferenceEnvironmentMember member)
    {
        var path = SourcePath(member.Path);

        if (member.Content is not null && node.Directories.Contains(path) || member.Content is null && node.Files.ContainsKey(path))
        {
            throw Invalid("A path cannot be both a file and a directory.");
        }

        for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
        {
            var directory = path[..slash];

            if (node.Files.ContainsKey(directory))
            {
                throw Invalid("A file cannot be used as a directory.");
            }

            node.Directories.Add(directory);
        }

        if (member.Content is null)
        {
            node.Directories.Add(path);
        }
        else
        {
            node.Files[path] = member.Content;
        }
    }

    private static string SourcePath(string path) => IsSourcePath(path) ? path : throw Invalid("A file path must stay inside its environment.");
    private static bool IsSourcePath(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Contains('\\') && !path.Contains('\0') && !path
        .Split('/')
        .Any(part => part is "" or "." or "..");
    private static UhpServerException Invalid(string message) => ReferenceBackend.Failure(422, "environment_invalid", message);
}
