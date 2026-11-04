namespace StaticNorth.Uhp.Core;

public readonly record struct UhpResponseStatus(string Value)
{
    private const string InProgressValue = "in_progress";
    private const string CompletedValue = "completed";
    private const string IncompleteValue = "incomplete";
    private const string FailedValue = "failed";
    private const string CancelledValue = "cancelled";

    public static UhpResponseStatus InProgress => new(InProgressValue);
    public static UhpResponseStatus Completed => new(CompletedValue);
    public static UhpResponseStatus Incomplete => new(IncompleteValue);
    public static UhpResponseStatus Failed => new(FailedValue);
    public static UhpResponseStatus Cancelled => new(CancelledValue);

    public bool IsTerminal => Value is CompletedValue or IncompleteValue or FailedValue or CancelledValue;

    public override string ToString() => Value;
}
