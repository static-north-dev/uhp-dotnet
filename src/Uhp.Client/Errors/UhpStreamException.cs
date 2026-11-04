using Uhp.Core;

namespace Uhp.Client;

public sealed class UhpStreamException(string message, string? responseId, long? lastSequenceNumber, string? idempotencyKey, Exception innerException)
    : UhpProtocolException(message, innerException)
{
    public string? ResponseId { get; } = responseId;
    public long? LastSequenceNumber { get; } = lastSequenceNumber;
    public string? IdempotencyKey { get; } = idempotencyKey;
    public bool ExecutionOutcomeUnknown => true;
}
