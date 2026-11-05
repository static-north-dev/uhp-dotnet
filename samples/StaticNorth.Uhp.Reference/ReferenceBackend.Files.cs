using System.Collections.Frozen;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.StaticFiles;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Server;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Reference;

public partial class ReferenceBackend : IUhpFileBackend
{
    private const long MaximumArtifactBytes = 67_108_864;
    private static readonly FileExtensionContentTypeProvider ArtifactTypes = new();
    private readonly Dictionary<string, ReferenceFileEntry> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Session, string Path), string> _artifactPaths = [];
    private readonly Dictionary<string, string> _artifactContainers = new(StringComparer.Ordinal);

    public async Task<Wire.File> UploadFileAsync(UhpRequestBody request, UhpRequestContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw Failure(400, "invalid_input", "An uploaded file requires a filename.");
        }
        using var content = new MemoryStream();
        await request.Content
            .CopyToAsync(content, ct)
            .ConfigureAwait(false);

        lock (_gate)
        {
            return AddFile(Owner(context), null, Path.GetFileName(request.FileName), content.ToArray(), request.ContentType);
        }
    }

    public Task<Wire.ListSessionFilesResponse> ListSessionFilesAsync(string sessionId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var owner = Owner(context);

        lock (_gate)
        {
            SessionStates(sessionId, owner);
            return Task.FromResult(new Wire.ListSessionFilesResponse
            {
                Files = _files.Values
                    .Where(entry => entry.Owner == owner && entry.SessionId == sessionId)
                    .Select(entry => entry.File)
                    .ToArray()
            });
        }
    }

    private async Task<IReadOnlyList<Wire.File>> CaptureArtifactsAsync(string sessionId, string owner, CancellationToken ct)
    {
        var workspace = Workspace(sessionId);

        if (!Directory.Exists(workspace))
        {
            return [];
        }

        List<Wire.File>? added = null;
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };

        foreach (var path in Directory.EnumerateFiles(workspace, "*", options))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(workspace, path);

            if (relative.StartsWith('.') || relative.Contains("/.", StringComparison.Ordinal))
            {
                continue;
            }

            var bytes = await ReferenceFileSystem
                .ReadAsync(workspace, relative, MaximumArtifactBytes, ct)
                .ConfigureAwait(false);

            if (bytes is null)
            {
                continue;
            }

            lock (_gate)
            {
                if (_deletingSessions.Contains(sessionId))
                {
                    break;
                }

                if (_artifactPaths.TryGetValue((sessionId, relative), out var id) && bytes
                    .AsSpan()
                    .SequenceEqual(_files[id].Content))
                {
                    continue;
                }

                var file = AddFile(owner, sessionId, relative, bytes,
                    ArtifactTypes.TryGetContentType(relative, out var mediaType) ? mediaType : "application/octet-stream");
                _artifactPaths[(sessionId, relative)] = file.Id;
                (added ??= []).Add(file);
            }
        }

        return added is null ? [] : added;
    }

    public async Task<UhpBinaryContent> DownloadSessionArchiveAsync(string sessionId, UhpRequestContext context, CancellationToken ct)
    {
        var files = await ListSessionFilesAsync(sessionId, context, ct)
            .ConfigureAwait(false);
        var buffer = new MemoryStream();

        try
        {
            await using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            {
                for (var index = 0; index < files.Files.Length; index++)
                {
                    var file = files.Files[index];
                    var source = await DownloadFileAsync(file.ContainerId!, file.Id, context, ct)
                        .ConfigureAwait(false);
                    await using var content = source.Content;
                    var entry = archive.CreateEntry(file.Filename);
                    await using var target = await entry
                        .OpenAsync(ct)
                        .ConfigureAwait(false);
                    await content
                        .CopyToAsync(target, ct)
                        .ConfigureAwait(false);
                }
            }

            buffer.Position = 0;
            return new(buffer, "application/zip", "artifacts.zip");
        }
        catch
        {
            await buffer
                .DisposeAsync()
                .ConfigureAwait(false);
            throw;
        }
    }

    public Task<UhpBinaryContent> DownloadFileAsync(string containerId, string fileId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_files.TryGetValue(fileId, out var entry) || entry.Owner != Owner(context) || entry.File.ContainerId != containerId)
            {
                throw UhpServerException.NotFound("file_not_found");
            }

            if (entry.SessionId is { } session)
            {
                SessionStates(session, entry.Owner);
            }

            return Task.FromResult(new UhpBinaryContent(new MemoryStream(entry.Content, false), entry.ContentType, entry.File.Filename));
        }
    }

    public Task<UhpBinaryContent> PreviewFileAsync(string containerId, string fileId, UhpRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        throw new UhpServerException(501, new("server_error", "preview_unavailable", "A preview converter is not configured."));
    }

    public Wire.File SeedArtifact(string sessionId, string owner, string name, ReadOnlySpan<byte> content, string contentType = "application/octet-stream")
    {
        lock (_gate)
        {
            SessionStates(sessionId, owner);
            return AddFile(owner, sessionId, name, content.ToArray(), contentType);
        }
    }

    private Wire.File AddFile(string owner, string? sessionId, string name, byte[] bytes, string contentType)
    {
        if (sessionId is null || !_artifactContainers.TryGetValue(sessionId, out var containerId))
        {
            containerId = $"cntr_{Guid.NewGuid():N}";

            if (sessionId is not null)
            {
                _artifactContainers.Add(sessionId, containerId);
            }
        }

        var file = new Wire.File
        {
            Id = $"file_{Guid.NewGuid():N}",
            ContainerId = containerId,
            Filename = name,
            Bytes = bytes.Length,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        _files.Add(file.Id, new(owner, sessionId, file, bytes, contentType));
        return file;
    }

    private IReadOnlyDictionary<string, ReferenceAttachment> Attachments(UhpTaskRequest request, string owner)
    {
        if ((request.InputFeatures & UhpInputFeatures.Files) == 0)
        {
            return FrozenDictionary<string, ReferenceAttachment>.Empty;
        }

        Dictionary<string, ReferenceAttachment>? attachments = null;

        foreach (var item in request.Input.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                var type = part.GetProperty("type");

                if (!(type.ValueEquals("input_file") || type.ValueEquals("input_image")) ||
                    !part.TryGetProperty("file_id", out var fileId) || fileId.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var id = fileId.GetString()!;

                if (!_files.TryGetValue(id, out var entry) || entry.Owner != owner)
                {
                    throw UhpServerException.NotFound("file_not_found");
                }

                attachments ??= new(StringComparer.Ordinal);
                attachments.TryAdd(id, new(entry.File.Filename, entry.Content, entry.ContentType));
            }
        }

        return attachments is null ? FrozenDictionary<string, ReferenceAttachment>.Empty : attachments;
    }

    private void DeleteSessionFiles(string sessionId)
    {
        _artifactContainers.Remove(sessionId);

        foreach (var key in _artifactPaths.Keys
            .Where(key => key.Session == sessionId)
            .ToArray())
        {
            _artifactPaths.Remove(key);
        }

        foreach (var id in _files.Values
            .Where(entry => entry.SessionId == sessionId)
            .Select(entry => entry.File.Id)
            .ToArray())
        {
            _files.Remove(id);
        }
    }

    private string Workspace(string sessionId) => Path.Combine(_runtime.WorkspaceRoot, sessionId);
}
