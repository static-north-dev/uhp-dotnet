using System.Text;
using System.Text.Json;

namespace StaticNorth.Uhp.Core.Providers;

public static class UhpHarnessTextInput
{
    public static void Validate(UhpTaskRequest task, string harness)
    {
        if (task.InputFeatures != UhpInputFeatures.None || task.Metadata?.ContainsKey("environment") == true ||
            task.Metadata?.ContainsKey("memory") == true)
        {
            throw UhpRequestException.InvalidRequest(422, "invalid_input", $"The {harness} preview accepts text without files, interactive answers, environments, or memories.");
        }

        if (task.Input.ValueKind == JsonValueKind.Array)
        {
            var hasText = false;

            foreach (var item in task.Input.EnumerateArray())
            {
                if (!item.GetProperty("role"u8).ValueEquals("user"u8))
                {
                    throw UhpRequestException.InvalidRequest(422, "invalid_input", $"The {harness} preview accepts user messages only.");
                }

                if (!hasText)
                {
                    hasText = HasText(item.GetProperty("content"u8));
                }
            }

            if (!hasText)
            {
                throw UhpRequestException.InvalidRequest(422, "invalid_input", $"{harness} requires a nonempty user prompt.");
            }
        }
    }

    public static string Prompt(JsonElement input)
    {
        if (input.ValueKind == JsonValueKind.String)
        {
            return input.GetString()!;
        }

        var prompt = new StringBuilder();

        foreach (var item in input.EnumerateArray())
        {
            var content = item.GetProperty("content"u8);

            if (content.ValueKind == JsonValueKind.String)
            {
                prompt.AppendLine(content.GetString());
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                prompt.AppendLine(part.GetProperty("text"u8).GetString());
            }
        }

        return prompt.ToString();
    }

    private static bool HasText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return !string.IsNullOrWhiteSpace(content.GetString());
        }

        foreach (var part in content.EnumerateArray())
        {
            if (!string.IsNullOrWhiteSpace(part.GetProperty("text"u8).GetString()))
            {
                return true;
            }
        }

        return false;
    }
}
