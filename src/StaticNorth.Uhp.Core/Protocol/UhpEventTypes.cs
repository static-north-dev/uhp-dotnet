namespace StaticNorth.Uhp.Core;

public static class UhpEventTypes
{
    public const string AnnotationAdded = "response.output_text.annotation.added";
    public const string ContentPartAdded = "response.content_part.added";
    public const string ContentPartDone = "response.content_part.done";
    public const string ErrorEvent = "error";
    public const string OutputItemAdded = "response.output_item.added";
    public const string OutputItemDone = "response.output_item.done";
    public const string ReasoningSummaryDelta = "response.reasoning_summary_text.delta";
    public const string ReasoningSummaryPartAdded = "response.reasoning_summary_part.added";
    public const string ReasoningSummaryPartDone = "response.reasoning_summary_part.done";
    public const string ResponseCompleted = "response.completed";
    public const string ResponseCreated = "response.created";
    public const string ResponseFailed = "response.failed";
    public const string ResponseInProgress = "response.in_progress";
    public const string ResponseIncomplete = "response.incomplete";
    public const string TextDelta = "response.output_text.delta";
    public const string TextDone = "response.output_text.done";
    public const string ToolArgumentsDelta = "response.function_call_arguments.delta";
    public const string ToolArgumentsDone = "response.function_call_arguments.done";
}
