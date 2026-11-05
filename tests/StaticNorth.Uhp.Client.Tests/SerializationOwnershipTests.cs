using System.Text.Json;
using StaticNorth.Uhp.Core;
using TUnit.Assertions;
using TUnit.Core;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class SerializationOwnershipTests
{
    [Test]
    public async Task EditedParsedEventUsesCurrentFieldsAndPreservesExtensions()
    {
        var parsed = (UhpTextDelta)UhpEventJson.Parse(TestData.Element("""{"type":"response.output_text.delta","sequence_number":1,"delta":"before","item_id":"i","output_index":0,"content_index":0,"vendor":{"keep":true}}"""));
        var edited = parsed with { Text = "after\nline", SequenceNumber = 2 };
        var json = UhpEventJson.Serialize(edited);

        await Assert
            .That(json
                .GetProperty("delta")
                .GetString())
            .IsEqualTo("after\nline");
        await Assert
            .That(json
                .GetProperty("sequence_number")
                .GetInt64())
            .IsEqualTo(2L);
        await Assert
            .That(json
                .GetProperty("vendor")
                .GetProperty("keep")
                .GetBoolean())
            .IsTrue();
    }

    [Test]
    public async Task EditedResponsePreservesNestedExtensionsWithoutRestoringClearedFields()
    {
        var parsed = UhpJson.ParseResponse(TestData.Element("""{"id":"r","object":"response","created_at":1,"status":"failed","model":"old","output":[{"type":"future","value":42}],"store":true,"usage":{"output_tokens":2,"vendor":7},"error":{"type":"t","code":"c","message":"old","detail":{"future":8},"vendor":9},"vendor":10}"""));
        var edited = parsed with
        {
            Model = "new",
            Store = null,
            Usage = parsed.Usage! with { OutputTokens = 3 },
            Error = parsed.Error! with { Message = "new" }
        };
        var json = JsonSerializer.SerializeToElement(edited, UhpJsonContext.Default.UhpResponse);

        await Assert
            .That(json
                .GetProperty("model")
                .GetString())
            .IsEqualTo("new");
        await Assert
            .That(json.TryGetProperty("store", out _))
            .IsFalse();
        await Assert
            .That(json
                .GetProperty("usage")
                .GetProperty("output_tokens")
                .GetInt64())
            .IsEqualTo(3L);
        await Assert
            .That(json
                .GetProperty("usage")
                .GetProperty("vendor")
                .GetInt32())
            .IsEqualTo(7);
        await Assert
            .That(json
                .GetProperty("error")
                .GetProperty("message")
                .GetString())
            .IsEqualTo("new");
        await Assert
            .That(json
                .GetProperty("error")
                .GetProperty("detail")
                .GetProperty("future")
                .GetInt32())
            .IsEqualTo(8);
        await Assert
            .That(json
                .GetProperty("error")
                .GetProperty("vendor")
                .GetInt32())
            .IsEqualTo(9);
        await Assert
            .That(json
                .GetProperty("vendor")
                .GetInt32())
            .IsEqualTo(10);
    }

    [Test]
    public async Task CatalogPreservesRootBackendAndModelExtensions()
    {
        var catalog = UhpJson.ParseModelCatalog(TestData.Element("""{"backends":{"b":{"default":"m","models":[{"id":"m","available":true,"vendor":7}],"vendor":8}},"vendor":9}"""));
        var json = JsonSerializer.SerializeToElement(catalog, UhpJsonContext.Default.UhpModelCatalog);

        await Assert
            .That(json
                .GetProperty("vendor")
                .GetInt32())
            .IsEqualTo(9);
        await Assert
            .That(json
                .GetProperty("backends")
                .GetProperty("b")
                .GetProperty("vendor")
                .GetInt32())
            .IsEqualTo(8);
        await Assert
            .That(json
                .GetProperty("backends")
                .GetProperty("b")
                .GetProperty("models")[0]
                .GetProperty("vendor")
                .GetInt32())
            .IsEqualTo(7);
    }
}
