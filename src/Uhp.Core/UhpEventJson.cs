using System.Text.Json;
using Uhp.Core.Serialization;

namespace Uhp.Core;

public static class UhpEventJson
{
    public static UhpEvent Parse(JsonElement value)
    {
        var wire = UhpJson.Read(value, WireJsonContext.Default.EventWire);
        UhpJson.RequireText(wire.Type, "event.type");

        if (wire.SequenceNumber < 0)
        {
            throw new UhpProtocolException("Event sequence numbers cannot be negative.");
        }

        UhpResponse Response() => UhpJson.ParseResponse(RequiredObject(value, "response"));
        UhpOutputItem Item() => new(RequiredObject(value, "item"));
        string ItemId() => RequiredText(wire.ItemId, "item_id");
        long OutputIndex() => RequiredIndex(wire.OutputIndex, "output_index");
        long ContentIndex() => RequiredIndex(wire.ContentIndex, "content_index");
        long SummaryIndex() => RequiredIndex(wire.SummaryIndex, "summary_index");

        UhpEvent result = wire.Type switch
        {
            "response.created" => new UhpResponseCreated(wire.SequenceNumber, Response()),
            "response.in_progress" => new UhpResponseInProgress(wire.SequenceNumber, Response()),
            "response.completed" => new UhpResponseCompleted(wire.SequenceNumber, Response()),
            "response.incomplete" => new UhpResponseIncomplete(wire.SequenceNumber, Response()),
            "response.failed" => new UhpResponseFailed(wire.SequenceNumber, Response()),
            "response.output_item.added" => new UhpOutputItemAdded(wire.SequenceNumber, Item(), OutputIndex()),
            "response.output_item.done" => new UhpOutputItemDone(wire.SequenceNumber, Item(), OutputIndex()),
            "response.output_text.delta" => new UhpTextDelta(wire.SequenceNumber, RequiredValue(wire.Delta, "delta"), ItemId(), OutputIndex(), ContentIndex()),
            "response.output_text.done" => new UhpTextDone(wire.SequenceNumber, RequiredValue(wire.Text, "text"), ItemId(), OutputIndex(), ContentIndex()),
            "response.content_part.added" => new UhpContentPartAdded(wire.SequenceNumber, RequiredObject(value, "part"), ItemId(), OutputIndex(), ContentIndex()),
            "response.content_part.done" => new UhpContentPartDone(wire.SequenceNumber, RequiredObject(value, "part"), ItemId(), OutputIndex(), ContentIndex()),
            "response.reasoning_summary_part.added" => new UhpReasoningSummaryPartAdded(wire.SequenceNumber, RequiredObject(value, "part"), ItemId(), OutputIndex(), SummaryIndex()),
            "response.reasoning_summary_part.done" => new UhpReasoningSummaryPartDone(wire.SequenceNumber, RequiredObject(value, "part"), ItemId(), OutputIndex(), SummaryIndex()),
            "response.reasoning_summary_text.delta" => new UhpReasoningSummaryDelta(wire.SequenceNumber, RequiredValue(wire.Delta, "delta"), ItemId(), OutputIndex(), SummaryIndex()),
            "response.function_call_arguments.delta" => new UhpToolArgumentsDelta(wire.SequenceNumber, RequiredValue(wire.Delta, "delta"), ItemId(), OutputIndex()),
            "response.function_call_arguments.done" => new UhpToolArgumentsDone(wire.SequenceNumber, RequiredValue(wire.Arguments, "arguments"), ItemId(), OutputIndex()),
            "response.output_text.annotation.added" => new UhpAnnotationAdded(wire.SequenceNumber, RequiredObject(value, "annotation"), ItemId(), OutputIndex(), ContentIndex(), RequiredIndex(wire.AnnotationIndex, "annotation_index")),
            "error" => new UhpErrorEvent(wire.SequenceNumber, RequiredText(wire.Code, "code"), RequiredValue(wire.Message, "message"), wire.Param),
            _ => new UhpUnknownEvent(wire.Type, wire.SequenceNumber, value.Clone())
        };

        return result with { Raw = value.Clone() };
    }

    public static JsonElement Serialize(UhpEvent update)
    {
        if (update.Raw.ValueKind == JsonValueKind.Object)
        {
            return update.Raw;
        }

        if (update is UhpUnknownEvent unknown)
        {
            var parsed = Parse(unknown.Data);

            if (parsed.Type != unknown.Type || parsed.SequenceNumber != unknown.SequenceNumber)
            {
                throw new UhpProtocolException("Unknown event JSON disagrees with its type or sequence number.");
            }

            return unknown.Data;
        }

        return UhpJson.CreateElement(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", update.Type);
            writer.WriteNumber("sequence_number", update.SequenceNumber);

            switch (update)
            {
                case UhpResponseEvent response:
                    writer.WritePropertyName("response");
                    UhpJson.SerializeResponse(response.Response).WriteTo(writer);
                    break;
                case UhpOutputItemAdded added:
                    WriteItem(writer, added.Item, added.OutputIndex);
                    break;
                case UhpOutputItemDone done:
                    WriteItem(writer, done.Item, done.OutputIndex);
                    break;
                case UhpTextDelta delta:
                    WriteReference(writer, delta.ItemId, delta.OutputIndex, delta.ContentIndex);
                    writer.WriteString("delta", delta.Text);
                    break;
                case UhpTextDone done:
                    WriteReference(writer, done.ItemId, done.OutputIndex, done.ContentIndex);
                    writer.WriteString("text", done.Text);
                    break;
                case UhpContentPartAdded part:
                    WritePart(writer, part.Part, part.ItemId, part.OutputIndex, part.ContentIndex);
                    break;
                case UhpContentPartDone part:
                    WritePart(writer, part.Part, part.ItemId, part.OutputIndex, part.ContentIndex);
                    break;
                case UhpReasoningSummaryPartAdded part:
                    WritePart(writer, part.Part, part.ItemId, part.OutputIndex, null, part.SummaryIndex);
                    break;
                case UhpReasoningSummaryPartDone part:
                    WritePart(writer, part.Part, part.ItemId, part.OutputIndex, null, part.SummaryIndex);
                    break;
                case UhpReasoningSummaryDelta delta:
                    WriteReference(writer, delta.ItemId, delta.OutputIndex, summaryIndex: delta.SummaryIndex);
                    writer.WriteString("delta", delta.Text);
                    break;
                case UhpToolArgumentsDelta delta:
                    WriteReference(writer, delta.ItemId, delta.OutputIndex);
                    writer.WriteString("delta", delta.Delta);
                    break;
                case UhpToolArgumentsDone done:
                    WriteReference(writer, done.ItemId, done.OutputIndex);
                    writer.WriteString("arguments", done.Arguments);
                    break;
                case UhpAnnotationAdded annotation:
                    WriteReference(writer, annotation.ItemId, annotation.OutputIndex, annotation.ContentIndex);
                    writer.WriteNumber("annotation_index", annotation.AnnotationIndex);
                    writer.WritePropertyName("annotation");
                    annotation.Annotation.WriteTo(writer);
                    break;
                case UhpErrorEvent error:
                    writer.WriteString("code", error.Code);
                    writer.WriteString("message", error.Message);
                    writer.WriteString("param", error.Param);
                    break;
                default:
                    throw new ArgumentException("Unsupported event implementation.", nameof(update));
            }

            writer.WriteEndObject();
        });
    }

    private static void WriteItem(Utf8JsonWriter writer, UhpOutputItem item, long outputIndex)
    {
        writer.WriteNumber("output_index", outputIndex);
        writer.WritePropertyName("item");
        item.Raw.WriteTo(writer);
    }

    private static void WritePart(Utf8JsonWriter writer, JsonElement part, string itemId, long outputIndex, long? contentIndex, long? summaryIndex = null)
    {
        WriteReference(writer, itemId, outputIndex, contentIndex, summaryIndex);
        writer.WritePropertyName("part");
        part.WriteTo(writer);
    }

    private static void WriteReference(Utf8JsonWriter writer, string itemId, long outputIndex, long? contentIndex = null, long? summaryIndex = null)
    {
        writer.WriteString("item_id", itemId);
        writer.WriteNumber("output_index", outputIndex);

        if (contentIndex is { } content)
        {
            writer.WriteNumber("content_index", content);
        }

        if (summaryIndex is { } summary)
        {
            writer.WriteNumber("summary_index", summary);
        }
    }

    private static string RequiredValue(string? value, string name) => value ?? throw new UhpProtocolException($"Missing event {name}.");

    private static string RequiredText(string? value, string name)
    {
        UhpJson.RequireText(value, name);
        return value!;
    }

    private static long RequiredIndex(long? value, string name) => value is >= 0 ? value.Value : throw new UhpProtocolException($"Invalid or missing event {name}.");

    private static JsonElement RequiredObject(JsonElement value, string name) => value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.Object
        ? result.Clone()
        : throw new UhpProtocolException($"Missing event {name} object.");
}
