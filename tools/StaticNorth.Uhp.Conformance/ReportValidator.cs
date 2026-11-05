using System.Globalization;
using System.Text.Json;

namespace StaticNorth.Uhp.Conformance;

internal static class ReportValidator
{
    public static void Validate(JsonElement report, JsonElement inventory)
    {
        Require(report
            .GetProperty("protocol")
            .GetString() == "uhp", "protocol");
        Require(report
            .GetProperty("requested_class")
            .GetString() == "full", "requested_class");
        Require(report
            .GetProperty("protocol_version")
            .GetString() == inventory
                .GetProperty("protocol_version")
                .GetString(), "protocol_version");
        Require(report
            .GetProperty("suite_protocol_version")
            .GetString() == inventory
                .GetProperty("protocol_version")
                .GetString(), "suite_protocol_version");
        Require(report
            .GetProperty("suite_version")
            .GetString() == inventory
                .GetProperty("suite_version")
                .GetString(), "suite_version");
        Require(report
            .GetProperty("conformant")
            .GetBoolean(), "conformant");
        Require(report
            .GetProperty("highest_class_passed")
            .GetString() == "full", "highest_class_passed");
        Require(DateTimeOffset.TryParseExact(report
            .GetProperty("generated_at")
            .GetString(), "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _), "generated_at");
        Require(report
            .GetProperty("skipped_not_verified")
            .GetArrayLength() == 0, "skipped_not_verified");

        var expected = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var check in inventory
            .GetProperty("checks")
            .EnumerateArray())
        {
            Require(expected.TryAdd(check
                .GetProperty("id")
                .GetString()!, check
                    .GetProperty("class")
                    .GetString()!), "duplicate inventory check");
        }

        var count = expected.Count;
        var coverage = report.GetProperty("coverage");
        Require(coverage
            .GetProperty("complete")
            .GetBoolean(), "coverage.complete");
        Require(coverage
            .GetProperty("required")
            .GetInt32() == count && coverage
                .GetProperty("reported")
                .GetInt32() == count, "coverage counts");
        Require(coverage
            .GetProperty("not_run")
            .GetArrayLength() == 0 && coverage
                .GetProperty("duplicate_or_unknown")
                .GetArrayLength() == 0, "coverage omissions");

        var summary = report.GetProperty("summary");
        Require(summary
            .GetProperty("pass")
            .GetInt32() == count && summary
                .GetProperty("total")
                .GetInt32() == count, "summary counts");

        foreach (var outcome in new[] { "fail", "error", "skip" })
        {
            Require(summary
                .GetProperty(outcome)
                .GetInt32() == 0, "summary " + outcome);
        }

        Require(report
            .GetProperty("checks")
            .GetArrayLength() == count, "checks count");

        foreach (var check in report
            .GetProperty("checks")
            .EnumerateArray())
        {
            Require(expected.Remove(check
                .GetProperty("id")
                .GetString()!, out var expectedClass), "unknown or duplicate check");
            Require(check
                .GetProperty("class")
                .GetString() == expectedClass, "check class");
            Require(check
                .GetProperty("outcome")
                .GetString() == "pass", "check outcome");
        }

        Require(expected.Count == 0, "missing checks");
    }

    internal static void Require(bool condition, string field)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Invalid conformance evidence: " + field + ".");
        }
    }
}
