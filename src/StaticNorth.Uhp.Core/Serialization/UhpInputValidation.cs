using System.Text.Json;

namespace StaticNorth.Uhp.Core.Serialization;

internal static class UhpInputValidation
{
    public static UhpInputFeatures Validate(JsonElement input)
    {
        if (input.ValueKind == JsonValueKind.String)
        {
            UhpJson.RequireText(input.GetString(), "input");
            return UhpInputFeatures.None;
        }

        if (input.ValueKind != JsonValueKind.Array || input.GetArrayLength() == 0)
        {
            throw new UhpProtocolException("Input must be text or a nonempty item array.");
        }

        var features = UhpInputFeatures.None;
        var answers = 0;

        foreach (var item in input.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new UhpProtocolException("Every input item must be an object.");
            }

            if (item.TryGetProperty("type", out var itemType) && itemType.ValueKind == JsonValueKind.String && itemType.ValueEquals("input_response"))
            {
                if (++answers > 1 || !HasText(item, "input_request_id") || !item.TryGetProperty("answer", out var answer) || answer.ValueKind != JsonValueKind.String)
                {
                    throw new UhpProtocolException("A continuation carries at most one valid input_response.");
                }

                features |= UhpInputFeatures.Interactive;
                continue;
            }

            if (!item.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String || !(role.ValueEquals("user") || role.ValueEquals("assistant") || role.ValueEquals("system") || role.ValueEquals("developer")) || !item.TryGetProperty("content", out var content))
            {
                throw new UhpProtocolException("Input messages require a supported role and content.");
            }

            if (content.ValueKind == JsonValueKind.String)
            {
                continue;
            }

            if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() == 0)
            {
                throw new UhpProtocolException("Message content must be text or a nonempty part array.");
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                {
                    throw new UhpProtocolException("Input parts require a type.");
                }

                if (type.ValueEquals("input_text") || type.ValueEquals("output_text"))
                {
                    if (!part.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                    {
                        throw new UhpProtocolException("Text parts require a text string.");
                    }
                }
                else if (type.ValueEquals("input_file") || type.ValueEquals("input_image"))
                {
                    if (!HasText(part, "file_id") && !HasText(part, "file_data") && !(type.ValueEquals("input_image") && HasText(part, "image_url")))
                    {
                        throw new UhpProtocolException("File and image parts require bytes or a file reference.");
                    }

                    features |= UhpInputFeatures.Files;
                }
                else
                {
                    throw new UhpProtocolException("Unsupported input part type.");
                }
            }
        }

        return features;
    }

    public static void ValidateContinuation(UhpInputFeatures features, string? previousResponseId)
    {
        if ((features & UhpInputFeatures.Interactive) != 0 && string.IsNullOrWhiteSpace(previousResponseId))
        {
            throw new UhpProtocolException("An input_response requires previous_response_id.");
        }
    }

    private static bool HasText(JsonElement value, string name) => value.TryGetProperty(name, out var text) && text.ValueKind == JsonValueKind.String && !text.ValueEquals("");
}
