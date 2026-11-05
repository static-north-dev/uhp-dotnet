using System.Text;
using System.Text.Json;

namespace StaticNorth.Uhp.Reference;

internal static class ReferenceNetworkPolicy
{
    private static readonly string[] Domains = ["pypi.org", "files.pythonhosted.org", "registry.npmjs.org", "nodejs.org", "github.com", "codeload.github.com", "objects.githubusercontent.com", "raw.githubusercontent.com"];

    public static void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("network"); writer.WriteBoolean("enabled", true); writer.WriteStartObject("domains");

        for (var index = 0; index < Domains.Length; index++)
        {
            writer.WriteString(Domains[index], "allow");
        }

        writer.WriteEndObject(); writer.WriteEndObject();
    }

    public static void WriteToml(StringBuilder text, string profile)
    {
        text
            .Append("[permissions.")
            .Append(profile)
            .AppendLine(".network]\nenabled = true");
        text
            .Append("[permissions.")
            .Append(profile)
            .AppendLine(".network.domains]");

        for (var index = 0; index < Domains.Length; index++)
        {
            text
                .Append('"')
                .Append(Domains[index])
                .AppendLine("\" = \"allow\"");
        }
    }
}
