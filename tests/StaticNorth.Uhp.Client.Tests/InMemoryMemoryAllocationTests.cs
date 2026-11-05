using System.Text.Json;
using StaticNorth.Uhp.TestSupport.Memory;

namespace StaticNorth.Uhp.Client.Tests;

[NotInParallel]
public sealed class InMemoryMemoryAllocationTests
{
    [Test]
    public async Task OwnedRecordReadDoesNotAllocateAfterWarmup()
    {
        IReferenceMemoryProvider provider = new InMemoryMemoryProvider();
        var memory = await provider.CreateMemoryAsync("alice");
        using var content = JsonDocument.Parse("\"text\"");
        var record = await provider.RememberAsync("alice", memory, new(content.RootElement));

        for (var index = 0; index < 1000; index++)
        {
            await provider.GetAsync("alice", memory, record.Id);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 1000; index++)
        {
            await provider.GetAsync("alice", memory, record.Id);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task RequiredOwnedWriteAllocationsStayWithinBudget()
    {
        IReferenceMemoryProvider provider = new InMemoryMemoryProvider();
        var memory = await provider.CreateMemoryAsync("alice");
        using var content = JsonDocument.Parse("\"text\"");
        var input = new ReferenceMemoryWrite(content.RootElement);

        for (var index = 0; index < 100; index++)
        {
            await provider.RememberAsync("alice", memory, input);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 1000; index++)
        {
            await provider.RememberAsync("alice", memory, input);
        }

        var bytesPerWrite = (GC.GetAllocatedBytesForCurrentThread() - before) / 1000;
        Console.WriteLine($"Reference memory retained write: {bytesPerWrite} B/write (budget: 2048 B/write).");
        await Assert.That(bytesPerWrite).IsGreaterThan(0L);
        await Assert.That(bytesPerWrite).IsLessThanOrEqualTo(2048L);
    }
}
