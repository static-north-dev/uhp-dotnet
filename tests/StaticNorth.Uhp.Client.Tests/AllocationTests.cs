using System.Buffers;
using System.Collections.Frozen;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using StaticNorth.Uhp.Core;
using TUnit.Assertions;
using TUnit.Core;

namespace StaticNorth.Uhp.Client.Tests;

[NotInParallel]
public sealed class AllocationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ResponseWriterDoesNotAllocateAfterWarmup(bool frozenMetadata)
    {
        var metadata = new Dictionary<string, JsonElement> { ["trace"] = TestData.Element("\"owned\"") };
        var parsed = UhpJson.ParseResponse(TestData.Element("""{"id":"r","object":"response","created_at":1,"status":"completed","model":"old","output":[{"type":"future","extra":true}],"usage":{"output_tokens":2,"vendor":7},"vendor":{"keep":true}}"""));
        var response = parsed with
        {
            Model = "edited",
            Metadata = frozenMetadata ? metadata.ToFrozenDictionary(StringComparer.Ordinal) : metadata,
            Usage = parsed.Usage! with { OutputTokens = 3 }
        };

        await AssertWriterAllocations(response, UhpJsonContext.Default.UhpResponse);
    }

    [Test]
    public async Task CatalogWriterDoesNotAllocateAfterWarmup()
    {
        var catalog = UhpJson.ParseModelCatalog(TestData.Element("""{"backends":{"b":{"default":"m","models":[{"id":"m","available":true,"vendor":7}],"vendor":8}},"vendor":9}"""));
        await AssertWriterAllocations(catalog, UhpJsonContext.Default.UhpModelCatalog);
    }

    [Test]
    public async Task EditedEventWriterDoesNotAllocateAfterWarmup()
    {
        var parsed = (UhpTextDelta)UhpEventJson.Parse(TestData.Element("""{"type":"response.output_text.delta","sequence_number":1,"delta":"old","item_id":"i","output_index":0,"content_index":0,"vendor":7}"""));
        UhpEvent edited = parsed with { Text = "new\nline", SequenceNumber = 2 };
        await AssertWriterAllocations(edited, UhpJsonContext.Default.UhpEvent);
    }

    [Test]
    [Arguments("discovery")]
    [Arguments("harness")]
    [Arguments("harness-models")]
    [Arguments("error")]
    [Arguments("cancellation")]
    public async Task OtherProtocolWritersDoNotAllocateAfterWarmup(string surface)
    {
        switch (surface)
        {
            case "discovery":
                await AssertWriterAllocations(UhpJson.ParseDiscovery(TestData.Element(TestData.Discovery())), UhpJsonContext.Default.UhpDiscovery);
                break;
            case "harness":
                await AssertWriterAllocations(UhpJson.ParseHarness(TestData.Element("""{"id":"h","name":"Harness","base":"runtime","plugins":[{"vendor":true}],"vendor":7}""")), UhpJsonContext.Default.UhpHarness);
                break;
            case "harness-models":
                await AssertWriterAllocations(UhpJson.ParseHarnessModels(TestData.Element("""{"models":[{"id":"m","available":true,"vendor":7}],"vendor":8}""")), UhpJsonContext.Default.UhpHarnessModels);
                break;
            case "error":
                await AssertWriterAllocations(UhpJson.ParseErrorValue(TestData.Element("""{"type":"t","code":"c","message":"m","detail":{"vendor":7},"vendor":8}""")), UhpJsonContext.Default.UhpError);
                break;
            case "cancellation":
                await AssertWriterAllocations(UhpJson.ParseSessionCancelResult(TestData.Element("""{"id":"s","status":"cancelled","vendor":7}""")), UhpJsonContext.Default.UhpSessionCancelResult);
                break;
        }
    }

    [Test]
    public async Task RequiredPayloadAllocationsStayWithinRecordedBudgets()
    {
        var request = new UhpTaskRequest("owned text") { HarnessId = "h", Metadata = new Dictionary<string, JsonElement> { ["trace"] = TestData.Element("1") } };
        var input = TestData.Element(TestData.Response);
        var payload = UhpJson.SerializeRequest(request, false);
        UhpEvent update = new UhpResponseCreated(0, UhpJson.ParseResponse(input));

        var requestBytes = MeasureRequired(() => _ = UhpJson.SerializeRequest(request, false));
        var responseBytes = MeasureRequired(() => _ = UhpJson.ParseResponse(input));
        var contentBytes = MeasureRequired(() =>
        {
            using var content = JsonContent.Create(payload, UhpJsonContext.Default.JsonElement);
        });
        var frameBytes = MeasureRequired(() => _ = JsonSerializer.SerializeToUtf8Bytes(update, UhpJsonContext.Default.UhpEvent));
        Console.WriteLine($"Required allocations B/operation: request snapshot={requestBytes}, parsed response={responseBytes}, HTTP JSON content={contentBytes}, SSE UTF-8 frame={frameBytes}.");

        await Assert.That(requestBytes).IsGreaterThan(0L).And.IsLessThanOrEqualTo(4096L);
        await Assert.That(responseBytes).IsGreaterThan(0L).And.IsLessThanOrEqualTo(8192L);
        await Assert.That(contentBytes).IsGreaterThan(0L).And.IsLessThanOrEqualTo(1024L);
        await Assert.That(frameBytes).IsGreaterThan(0L).And.IsLessThanOrEqualTo(4096L);
    }

    [Test]
    public async Task RepeatedProtocolChecksDoNotAllocate()
    {
        const int iterations = 1024;
        var valid = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < iterations; index++)
        {
            if (UhpProtocol.IsValidIdempotencyKey("key-123") && UhpProtocol.IsJsonContentType("Application/Json; charset=utf-8"))
            {
                valid++;
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(valid).IsEqualTo(iterations);
        await Assert.That(allocated).IsEqualTo(0L);
    }

    private static long MeasureRequired(Action operation)
    {
        const int iterations = 128;

        for (var index = 0; index < 512; index++)
        {
            operation();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < iterations; index++)
        {
            operation();
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    private static async Task AssertWriterAllocations<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        const int iterations = 256;
        var buffer = new ArrayBufferWriter<byte>(16 * 1024);
        using var writer = new Utf8JsonWriter(buffer);

        for (var index = 0; index < 512; index++)
        {
            buffer.Clear();
            writer.Reset(buffer);
            JsonSerializer.Serialize(writer, value, typeInfo);
            writer.Flush();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < iterations; index++)
        {
            buffer.Clear();
            writer.Reset(buffer);
            JsonSerializer.Serialize(writer, value, typeInfo);
            writer.Flush();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"{typeof(T).Name}: {allocated / iterations} B/write after warmup; caller-owned buffer.");
        await Assert.That(allocated).IsEqualTo(0L);
    }
}
