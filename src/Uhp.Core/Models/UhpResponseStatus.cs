namespace Uhp.Core;

public readonly record struct UhpResponseStatus(string Value)
{
    public static UhpResponseStatus InProgress => new("in_progress");
    public static UhpResponseStatus Completed => new("completed");
    public static UhpResponseStatus Incomplete => new("incomplete");
    public static UhpResponseStatus Failed => new("failed");
    public static UhpResponseStatus Cancelled => new("cancelled");

    public bool IsTerminal => Value is "completed" or "incomplete" or "failed" or "cancelled";

    public override string ToString() => Value;
}
