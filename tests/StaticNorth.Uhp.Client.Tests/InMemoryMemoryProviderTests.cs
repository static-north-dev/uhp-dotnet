using System.Text.Json;
using StaticNorth.Uhp.TestSupport.Memory;

namespace StaticNorth.Uhp.Client.Tests;

public sealed class InMemoryMemoryProviderTests
{
    [Test]
    public async Task TextShorthandProducesAnOwnedUntrustedRecord()
    {
        IReferenceMemoryProvider provider = new InMemoryMemoryProvider();
        var memory = await provider.CreateMemoryAsync("alice");
        ReferenceMemoryRecord record;

        using (var content = JsonDocument.Parse("\"remember this\""))
        {
            record = await provider.RememberAsync("alice", memory, new(content.RootElement));
        }

        await Assert
            .That(record.Content[0]
                .GetProperty("text")
                .GetString())
            .IsEqualTo("remember this");
        await Assert
            .That(record.Content[0]
                .GetProperty("type")
                .GetString())
            .IsEqualTo("text");
        await Assert
            .That(record.Content[0].TryGetProperty("text_source", out _))
            .IsFalse();
        await Assert
            .That(record.WrittenBy)
            .IsEqualTo("alice");
        await Assert
            .That(record.Trust)
            .IsEqualTo("untrusted");
        await Assert
            .That(record.Version)
            .IsEqualTo(1L);
    }

    [Test]
    public async Task PartsAttributesAndReferencesAreOwnedAndPreserveExtensions()
    {
        var provider = new InMemoryMemoryProvider();
        var memory = await provider.CreateMemoryAsync("alice");
        ReferenceMemoryRecord record;

        using (var document = JsonDocument.Parse("""{"parts":[{"type":"text","text":"first"},{"type":"x.widget","value":{"future":42}}],"attributes":{"nested":{"count":2}},"reference":{"record_id":"external","rel":"subject"}}"""))
        {
            var attributes = new Dictionary<string, JsonElement>
            {
                ["nested"] = document.RootElement
                    .GetProperty("attributes")
                    .GetProperty("nested")
            };
            var references = new List<JsonElement> { document.RootElement.GetProperty("reference") };
            record = await provider.RememberAsync("alice", memory, new(document.RootElement.GetProperty("parts")) { Attributes = attributes, References = references });
            attributes.Clear();
            references.Clear();
        }

        await Assert
            .That(record.Content.Count)
            .IsEqualTo(2);
        await Assert
            .That(record.Content[1]
                .GetProperty("value")
                .GetProperty("future")
                .GetInt32())
            .IsEqualTo(42);
        await Assert
            .That(record.Attributes["nested"]
                .GetProperty("count")
                .GetInt32())
            .IsEqualTo(2);
        await Assert
            .That(record.References[0]
                .GetProperty("record_id")
                .GetString())
            .IsEqualTo("external");
    }

    [Test]
    [Arguments("read")]
    [Arguments("list")]
    [Arguments("write")]
    [Arguments("revise")]
    [Arguments("history")]
    [Arguments("forget")]
    [Arguments("erase")]
    [Arguments("delete")]
    public async Task EveryOperationRefusesAnotherOwner(string operation)
    {
        var provider = new InMemoryMemoryProvider();
        var memory = await provider.CreateMemoryAsync("alice");
        var record = await provider.RememberAsync("alice", memory, Write("private"));

        await Assert
            .That((Func<Task>)(async () =>
        {
            switch (operation)
            {
                case "read":
                    await provider.GetAsync("bob", memory, record.Id);
                    break;
                case "list":
                    await provider.ListAsync("bob", memory);
                    break;
                case "write":
                    await provider.RememberAsync("bob", memory, Write("changed"));
                    break;
                case "revise":
                    await provider.ReviseAsync("bob", memory, record.Id, new());
                    break;
                case "history":
                    await provider.HistoryAsync("bob", memory, record.Id);
                    break;
                case "forget":
                    await provider.ForgetAsync("bob", memory, record.Id);
                    break;
                case "erase":
                    await provider.EraseAsync("bob", memory, record.Id);
                    break;
                case "delete":
                    await provider.DeleteMemoryAsync("bob", memory);
                    break;
            }
        }))
            .ThrowsExactly<KeyNotFoundException>();

        await Assert
            .That((await provider.GetAsync("alice", memory, record.Id)).Version)
            .IsEqualTo(1L);
    }

    [Test]
    public async Task RecordCannotBeReadThroughAnotherMemory()
    {
        var provider = new InMemoryMemoryProvider();
        var first = await provider.CreateMemoryAsync("alice");
        var second = await provider.CreateMemoryAsync("alice");
        var record = await provider.RememberAsync("alice", first, Write("private"));
        await Assert
            .That((Func<Task>)(async () => await provider.GetAsync("alice", second, record.Id)))
            .ThrowsExactly<KeyNotFoundException>();
    }

    [Test]
    public async Task RevisionAppendsHistoryWithoutChangingExistingSnapshots()
    {
        var provider = new InMemoryMemoryProvider();
        var memory = await provider.CreateMemoryAsync("alice");
        var first = await provider.RememberAsync("alice", memory, Write("March"));
        var before = await provider.HistoryAsync("alice", memory, first.Id);
        ReferenceMemoryRecord second;

        using (var content = JsonDocument.Parse("\"April\""))
        {
            second = await provider.ReviseAsync("alice", memory, first.Id, new() { Content = content.RootElement });
        }

        var history = await provider.HistoryAsync("alice", memory, first.Id);
        await Assert
            .That(second.Id)
            .IsEqualTo(first.Id);
        await Assert
            .That(second.Version)
            .IsEqualTo(2L);
        await Assert
            .That(second.Supersedes)
            .IsEqualTo(1L);
        await Assert
            .That(history[0].Status)
            .IsEqualTo("superseded");
        await Assert
            .That(history[0].Content[0]
                .GetProperty("text")
                .GetString())
            .IsEqualTo("March");
        await Assert
            .That(history[1].Content[0]
                .GetProperty("text")
                .GetString())
            .IsEqualTo("April");
        await Assert
            .That(first.Status)
            .IsEqualTo("active");
        await Assert
            .That(before.Count)
            .IsEqualTo(1);
    }

    [Test]
    public async Task InvalidRevisionDoesNotChangeHistory()
    {
        var provider = new InMemoryMemoryProvider();
        var memory = await provider.CreateMemoryAsync("alice");
        var first = await provider.RememberAsync("alice", memory, Write("original"));
        using var invalid = JsonDocument.Parse("42");
        await Assert
            .That((Func<Task>)(async () => await provider.ReviseAsync("alice", memory, first.Id, new() { Content = invalid.RootElement })))
            .ThrowsExactly<ArgumentException>();
        await Assert
            .That((await provider.HistoryAsync("alice", memory, first.Id)).Count)
            .IsEqualTo(1);
        await Assert
            .That((await provider.GetAsync("alice", memory, first.Id)).Status)
            .IsEqualTo("active");
    }

    [Test]
    public async Task ForgetKeepsHistoryWhileEraseRemovesEveryVersion()
    {
        var provider = new InMemoryMemoryProvider();
        var memory = await provider.CreateMemoryAsync("alice");
        var record = await provider.RememberAsync("alice", memory, Write("private"));
        await provider.ForgetAsync("alice", memory, record.Id);
        await provider.ForgetAsync("alice", memory, record.Id);
        await Assert
            .That((await provider.ListAsync("alice", memory)).Count)
            .IsEqualTo(0);
        await Assert
            .That((Func<Task>)(async () => await provider.GetAsync("alice", memory, record.Id)))
            .ThrowsExactly<KeyNotFoundException>();
        var history = await provider.HistoryAsync("alice", memory, record.Id);
        await Assert
            .That(history.Count)
            .IsEqualTo(2);
        await Assert
            .That(history[1].Status)
            .IsEqualTo("forgotten");
        await provider.EraseAsync("alice", memory, record.Id);
        await provider.EraseAsync("alice", memory, record.Id);
        await Assert
            .That((Func<Task>)(async () => await provider.HistoryAsync("alice", memory, record.Id)))
            .ThrowsExactly<KeyNotFoundException>();
    }

    [Test]
    public async Task DeleteRemovesOnlyTheSelectedMemory()
    {
        var provider = new InMemoryMemoryProvider();
        var first = await provider.CreateMemoryAsync("alice");
        var second = await provider.CreateMemoryAsync("alice");
        var record = await provider.RememberAsync("alice", first, Write("private"));
        await provider.DeleteMemoryAsync("alice", first);
        await Assert
            .That((Func<Task>)(async () => await provider.HistoryAsync("alice", first, record.Id)))
            .ThrowsExactly<KeyNotFoundException>();
        await Assert
            .That((await provider.ListAsync("alice", second)).Count)
            .IsEqualTo(0);
    }

    [Test]
    public async Task CancelledCallsDoNotMutateState()
    {
        var provider = new InMemoryMemoryProvider();
        var memory = await provider.CreateMemoryAsync("alice");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert
            .That((Func<Task>)(async () => await provider.RememberAsync("alice", memory, Write("private"), cancelled.Token)))
            .ThrowsExactly<OperationCanceledException>();
        await Assert
            .That((await provider.ListAsync("alice", memory)).Count)
            .IsEqualTo(0);
    }

    [Test]
    public async Task TitleOnlyRecordsAreSupported()
    {
        var provider = new InMemoryMemoryProvider();
        var memory = await provider.CreateMemoryAsync("alice");
        var record = await provider.RememberAsync("alice", memory, new(default) { Title = "A fact" });
        await Assert
            .That(record.Content.Count)
            .IsEqualTo(0);
        await Assert
            .That(record.Title)
            .IsEqualTo("A fact");
    }

    [Test]
    public async Task SeparateInstancesDoNotShareState()
    {
        var first = new InMemoryMemoryProvider();
        var second = new InMemoryMemoryProvider();
        var memory = await first.CreateMemoryAsync("alice");
        await Assert
            .That((Func<Task>)(async () => await second.ListAsync("alice", memory)))
            .ThrowsExactly<KeyNotFoundException>();
    }

    [Test]
    public async Task ConcurrentRevisionsPreserveEveryVersion()
    {
        var provider = new InMemoryMemoryProvider();
        var memory = await provider.CreateMemoryAsync("alice");
        var record = await provider.RememberAsync("alice", memory, Write("original"));
        await Parallel.ForEachAsync(Enumerable.Range(0, 64), new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (_, ct) =>
        {
            await provider.ReviseAsync("alice", memory, record.Id, new(), ct);
        });

        var history = await provider.HistoryAsync("alice", memory, record.Id);
        await Assert
            .That(history.Count)
            .IsEqualTo(65);

        for (var index = 0; index < history.Count; index++)
        {
            await Assert
                .That(history[index].Version)
                .IsEqualTo(index + 1L);
            await Assert
                .That(history[index].Status)
                .IsEqualTo(index == history.Count - 1 ? "active" : "superseded");
        }
    }

    [Test]
    [Arguments("""[{"type":"text"}]""")]
    [Arguments("""[{"type":"image","url":"x"}]""")]
    [Arguments("""[{"type":"text","text":"v","role":"developer"}]""")]
    [Arguments("""[{"type":"file","file":{}}]""")]
    [Arguments("""[{"type":"file","file":{"id":"file_1","bytes":-1}}]""")]
    [Arguments("""[{"type":"text","text":"v","role":[]}]""")]
    [Arguments("""[{"type":"text","text":"v","text_source":"guessed"}]""")]
    [Arguments("""[{"type":"x."}]""")]
    public async Task MalformedPartsAreRejectedWithoutMutatingRecords(string json)
    {
        var provider = new InMemoryMemoryProvider();
        var memory = await provider.CreateMemoryAsync("alice");
        var original = await provider.RememberAsync("alice", memory, Write("original"));
        using var invalid = JsonDocument.Parse(json);
        await Assert
            .That((Func<Task>)(async () => await provider.RememberAsync("alice", memory, new(invalid.RootElement))))
            .ThrowsExactly<ArgumentException>();
        await Assert
            .That((Func<Task>)(async () => await provider.ReviseAsync("alice", memory, original.Id, new() { Content = invalid.RootElement })))
            .ThrowsExactly<ArgumentException>();
        await Assert
            .That((await provider.ListAsync("alice", memory)).Count)
            .IsEqualTo(1);
        await Assert
            .That((await provider.HistoryAsync("alice", memory, original.Id)).Count)
            .IsEqualTo(1);
        await Assert
            .That((await provider.GetAsync("alice", memory, original.Id)).Status)
            .IsEqualTo("active");
    }

    [Test]
    public async Task FileReferencesRemainOwnedAndInOrder()
    {
        var provider = new InMemoryMemoryProvider();
        var memory = await provider.CreateMemoryAsync("alice");
        ReferenceMemoryRecord record;

        using (var content = JsonDocument.Parse("""[{"type":"text","text":"See attachment"},{"type":"file","file":{"id":"file_1","name":"a.txt","media_type":"text/plain","bytes":3},"text":"abc","text_source":"stated"}]"""))
        {
            record = await provider.RememberAsync("alice", memory, new(content.RootElement));
        }

        await Assert
            .That(record.Content.Count)
            .IsEqualTo(2);
        await Assert
            .That(record.Content[0]
                .GetProperty("text")
                .GetString())
            .IsEqualTo("See attachment");
        await Assert
            .That(record.Content[1]
                .GetProperty("file")
                .GetProperty("id")
                .GetString())
            .IsEqualTo("file_1");
        await Assert
            .That(record.Content[1]
                .GetProperty("file")
                .GetProperty("bytes")
                .GetInt32())
            .IsEqualTo(3);
    }

    private static ReferenceMemoryWrite Write(string text)
    {
        using var document = JsonDocument.Parse("\"" + text + "\"");
        return new ReferenceMemoryWrite(document.RootElement.Clone());
    }
}
