namespace StaticNorth.Uhp.Client;

public sealed class UhpSubmissionException(string? idempotencyKey, Exception innerException)
    : HttpRequestException("The task submission outcome is unknown. Recover a known response ID or use an explicitly supported same-key retry.", innerException)
{
    public string? IdempotencyKey { get; } = idempotencyKey;
    public bool ExecutionOutcomeUnknown => true;
}
