using System.Text.Json;
using System.Text.Json.Nodes;
using StaticNorth.Uhp.Conformance;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class ConformanceReportTests
{
    [Test]
    public async Task AcceptsCompleteReport()
    {
        using var inventory = JsonDocument.Parse("""{"protocol_version":"2026-10-19","suite_version":"2026.10.19.post5","checks":[{"id":"D-01","class":"core"}]}""");
        using var report = JsonDocument.Parse(CreateReport()
            .ToJsonString());
        ReportValidator.Validate(report.RootElement, inventory.RootElement);
        await Assert
            .That(report.RootElement
                .GetProperty("conformant")
                .GetBoolean())
            .IsTrue();
    }

    [Test]
    [Arguments("skip")]
    [Arguments("fail")]
    [Arguments("error")]
    [Arguments("duplicate")]
    [Arguments("missing")]
    [Arguments("class")]
    [Arguments("version")]
    [Arguments("suite")]
    [Arguments("coverage")]
    [Arguments("outcome")]
    [Arguments("summary")]
    [Arguments("timestamp")]
    [Arguments("subset")]
    public async Task RejectsMisleadingGreenReports(string defect)
    {
        using var inventory = JsonDocument.Parse("""{"protocol_version":"2026-10-19","suite_version":"2026.10.19.post5","checks":[{"id":"D-01","class":"core"}]}""");
        var report = CreateReport();

        switch (defect)
        {
            case "skip":
            case "fail":
            case "error":
                report["summary"]![defect] = 1;
                break;
            case "duplicate":
                report["checks"]!
                    .AsArray()
                    .Add(report["checks"]![0]!.DeepClone());
                break;
            case "missing":
                report["checks"]!
                    .AsArray()
                    .Clear();
                break;
            case "class":
                report["checks"]![0]!["class"] = "full";
                break;
            case "version":
                report["protocol_version"] = "2026-09-12";
                break;
            case "suite":
                report["suite_version"] = "unverified";
                break;
            case "coverage":
                report["coverage"]!["complete"] = false;
                break;
            case "outcome":
                report["checks"]![0]!["outcome"] = "skip";
                break;
            case "summary":
                report["summary"]!["pass"] = 104;
                break;
            case "timestamp":
                report["generated_at"] = "unknown";
                break;
            case "subset":
                report["requested_class"] = "core";
                break;
        }

        using var document = JsonDocument.Parse(report.ToJsonString());
        await Assert
            .That(() => ReportValidator.Validate(document.RootElement, inventory.RootElement))
            .ThrowsExactly<InvalidOperationException>();
    }

    private static JsonObject CreateReport() => JsonNode.Parse("""
        {"protocol":"uhp","protocol_version":"2026-10-19","suite_protocol_version":"2026-10-19",
         "suite_version":"2026.10.19.post5","requested_class":"full","conformant":true,
         "highest_class_passed":"full","generated_at":"2026-11-04T00:00:00Z","skipped_not_verified":[],
         "coverage":{"complete":true,"required":1,"reported":1,"not_run":[],"duplicate_or_unknown":[]},
         "summary":{"pass":1,"total":1,"fail":0,"error":0,"skip":0},
         "checks":[{"id":"D-01","class":"core","outcome":"pass"}]}
        """)!.AsObject();
}
