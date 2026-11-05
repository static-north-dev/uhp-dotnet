using System.Text;
using System.Text.Json;
using StaticNorth.Uhp.Core;

namespace StaticNorth.Uhp.Reference;

internal static class CodexInput
{
    public static async Task<JsonElement> PrepareAsync(ReferenceRuntimeRequest request, string? secretReference, string? prime, CancellationToken ct)
    {
        var text = new StringBuilder();
        List<(string Type, string Value)>? images = null;

        if (prime is not null)
        {
            text
                .AppendLine(prime)
                .AppendLine("End of untrusted memory data. The actual task follows.");
        }

        if (request.Answer is { } answer)
        {
            text
                .Append("The person's answer to ")
                .Append(answer.RequestId)
                .Append(" (\"")
                .Append(answer.Prompt)
                .AppendLine("\"):");

            if (secretReference is not null)
            {
                text
                    .Append("The secret is held by the harness as ")
                    .Append(secretReference)
                    .AppendLine(". Its value is outside the conversation. Use the reference with a secure harness tool if needed.");
            }
            else
            {
                text.AppendLine(answer.Value);
            }
        }

        if (request.Task.Instructions is { } instructions)
        {
            text.AppendLine(instructions);
        }

        var input = request.Task.Input;

        if (input.ValueKind == JsonValueKind.String)
        {
            text.Append(input.GetString());
        }
        else
        {
            foreach (var item in input.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var type) && type.ValueEquals("input_response"))
                {
                    continue;
                }

                var content = item.GetProperty("content");

                if (content.ValueKind == JsonValueKind.String)
                {
                    text.AppendLine(content.GetString());
                    continue;
                }

                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var partText))
                    {
                        text.AppendLine(partText.GetString());
                        continue;
                    }

                    var isImage = part
                        .GetProperty("type")
                        .ValueEquals("input_image");

                    if (isImage && part.TryGetProperty("image_url", out var imageUrl) && imageUrl.ValueKind == JsonValueKind.String)
                    {
                        images ??= [];
                        images.Add(("image", imageUrl.GetString()!));
                        continue;
                    }

                    ReadOnlyMemory<byte> bytes;
                    string name;

                    if (part.TryGetProperty("file_id", out var fileId) && fileId.ValueKind == JsonValueKind.String)
                    {
                        if (!request.Attachments.TryGetValue(fileId.GetString()!, out var attachment))
                        {
                            throw ReferenceBackend.Failure(404, "file_not_found", "The input file is not available to this principal.");
                        }

                        bytes = attachment.Content;
                        name = attachment.FileName;
                    }
                    else
                    {
                        var data = part
                            .GetProperty("file_data")
                            .GetString()!;
                        var comma = data.IndexOf(',');

                        if (!data.StartsWith("data:", StringComparison.Ordinal) || comma < 0 || !data
                            .AsSpan(0, comma)
                            .EndsWith(";base64", StringComparison.Ordinal))
                        {
                            throw ReferenceBackend.Failure(400, "invalid_input", "An inline file requires a base64 data URL.");
                        }

                        bytes = Convert.FromBase64String(data[(comma + 1)..]);
                        name = part.TryGetProperty("filename", out var filename) && filename.ValueKind == JsonValueKind.String ? filename.GetString()! : "attachment";
                    }

                    var path = Path.Combine(request.Workspace, $".uhp-input-{Guid.NewGuid():N}-" + Path.GetFileName(name));
                    await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16_384, FileOptions.Asynchronous);
                    await file
                        .WriteAsync(bytes, ct)
                        .ConfigureAwait(false);

                    if (isImage)
                    {
                        images ??= [];
                        images.Add(("localImage", path));
                    }
                    else
                    {
                        text
                            .Append("Attached input file: ")
                            .AppendLine(path);
                    }
                }
            }
        }

        return UhpJson.CreateElement(writer =>
        {
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", text.ToString());
            writer.WriteEndObject();

            if (images is not null)
            {
                for (var index = 0; index < images.Count; index++)
                {
                    var image = images[index];
                    writer.WriteStartObject();
                    writer.WriteString("type", image.Type);
                    writer.WriteString(image.Type == "image" ? "url" : "path", image.Value);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
        });
    }
}
