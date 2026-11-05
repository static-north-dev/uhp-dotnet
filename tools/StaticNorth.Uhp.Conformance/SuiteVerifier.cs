using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StaticNorth.Uhp.Conformance;

internal static partial class SuiteVerifier
{
    public static async Task VerifyAsync(string checkout, JsonElement manifest, JsonElement inventory, CancellationToken ct)
    {
        VerifyInventory(manifest, inventory);

        foreach (var file in manifest
            .GetProperty("files")
            .EnumerateObject())
        {
            var bytes = await File
                .ReadAllBytesAsync(Path.Combine(checkout, file.Name), ct)
                .ConfigureAwait(false);
            ReportValidator.Require(Convert.ToHexStringLower(SHA256.HashData(bytes)) == file.Value.GetString(), "suite file hash: " + file.Name);
        }

        var source = await File
            .ReadAllTextAsync(Path.Combine(checkout, "protocol/conformance/uhp_conformance/checks.py"), ct)
            .ConfigureAwait(false);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var check in inventory
            .GetProperty("checks")
            .EnumerateArray())
        {
            ReportValidator.Require(expected.TryAdd(check
                .GetProperty("id")
                .GetString()!, check
                    .GetProperty("class")
                    .GetString()!), "duplicate inventory check");
        }

        var matches = CheckDeclaration()
            .Matches(source);
        ReportValidator.Require(matches.Count == expected.Count, "registry count");

        foreach (Match match in matches)
        {
            ReportValidator.Require(expected.Remove(match.Groups["id"].Value, out var expectedClass) && expectedClass == match.Groups["class"].Value, "registry check identity/class");
        }

        ReportValidator.Require(expected.Count == 0, "missing registry checks");
    }

    public static void VerifyInventory(JsonElement manifest, JsonElement inventory)
    {
        ReportValidator.Require(manifest
            .GetProperty("commit")
            .GetString() == inventory
                .GetProperty("upstream_commit")
                .GetString(), "inventory upstream commit");
        ReportValidator.Require(manifest
            .GetProperty("suite_version")
            .GetString() == inventory
                .GetProperty("suite_version")
                .GetString(), "inventory suite version");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(inventory.GetRawText()));
        ReportValidator.Require(Convert.ToHexStringLower(hash) == manifest
            .GetProperty("inventory_sha256")
            .GetString(), "inventory hash");
    }

    [GeneratedRegex("@check\\(\\\"(?<id>[^\\\"]+)\\\",\\s*\\\"[^\\\"]+\\\",\\s*\\\"(?<class>core|extended|full)\\\"")]
    private static partial Regex CheckDeclaration();
}
