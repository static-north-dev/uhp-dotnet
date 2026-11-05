using System.Text.Json;
using StaticNorth.Uhp.Core.Serialization;

namespace StaticNorth.Uhp.Core;

public static class UhpEventJson
{
    public static UhpEvent Parse(JsonElement value)
    {
        value = value.Clone();
        var wire = UhpJson.Read(value, WireJsonContext.Default.EventWire);
        UhpJson.RequireText(wire.Type, "event.type");

        if (wire.SequenceNumber < 0)
        {
            throw new UhpProtocolException("Event sequence numbers cannot be negative.");
        }

        UhpEvent result = wire.Type switch
        {
            UhpEventTypes.ResponseCreated => new UhpResponseCreated(wire.SequenceNumber, Response()),
            UhpEventTypes.ResponseInProgress => new UhpResponseInProgress(wire.SequenceNumber, Response()),
            UhpEventTypes.ResponseCompleted => new UhpResponseCompleted(wire.SequenceNumber, Response()),
            UhpEventTypes.ResponseIncomplete => new UhpResponseIncomplete(wire.SequenceNumber, Response()),
            UhpEventTypes.ResponseFailed => new UhpResponseFailed(wire.SequenceNumber, Response()),
            UhpEventTypes.OutputItemAdded => new UhpOutputItemAdded(wire.SequenceNumber, Item(), OutputIndex()),
            UhpEventTypes.OutputItemDone => new UhpOutputItemDone(wire.SequenceNumber, Item(), OutputIndex()),
            UhpEventTypes.TextDelta => new UhpTextDelta(wire.SequenceNumber, RequiredValue(wire.Delta, UhpJsonFields.Delta), ItemId(), OutputIndex(), ContentIndex()),
            UhpEventTypes.TextDone => new UhpTextDone(wire.SequenceNumber, RequiredValue(wire.Text, UhpJsonFields.Text), ItemId(), OutputIndex(), ContentIndex()),
            UhpEventTypes.ContentPartAdded => new UhpContentPartAdded(wire.SequenceNumber, RequiredObject(value, UhpJsonFields.Part), ItemId(), OutputIndex(), ContentIndex()),
            UhpEventTypes.ContentPartDone => new UhpContentPartDone(wire.SequenceNumber, RequiredObject(value, UhpJsonFields.Part), ItemId(), OutputIndex(), ContentIndex()),
            UhpEventTypes.ReasoningSummaryPartAdded => new UhpReasoningSummaryPartAdded(wire.SequenceNumber, RequiredObject(value, UhpJsonFields.Part), ItemId(), OutputIndex(), SummaryIndex()),
            UhpEventTypes.ReasoningSummaryPartDone => new UhpReasoningSummaryPartDone(wire.SequenceNumber, RequiredObject(value, UhpJsonFields.Part), ItemId(), OutputIndex(), SummaryIndex()),
            UhpEventTypes.ReasoningSummaryDelta => new UhpReasoningSummaryDelta(wire.SequenceNumber, RequiredValue(wire.Delta, UhpJsonFields.Delta), ItemId(), OutputIndex(), SummaryIndex()),
            UhpEventTypes.ToolArgumentsDelta => new UhpToolArgumentsDelta(wire.SequenceNumber, RequiredValue(wire.Delta, UhpJsonFields.Delta), ItemId(), OutputIndex()),
            UhpEventTypes.ToolArgumentsDone => new UhpToolArgumentsDone(wire.SequenceNumber, RequiredValue(wire.Arguments, UhpJsonFields.Arguments), ItemId(), OutputIndex()),
            UhpEventTypes.AnnotationAdded => new UhpAnnotationAdded(wire.SequenceNumber, RequiredObject(value, UhpJsonFields.Annotation), ItemId(), OutputIndex(), ContentIndex(), RequiredIndex(wire.AnnotationIndex, UhpJsonFields.AnnotationIndex)),
            UhpEventTypes.ErrorEvent => new UhpErrorEvent(wire.SequenceNumber, RequiredText(wire.Code, UhpJsonFields.Code), RequiredValue(wire.Message, UhpJsonFields.Message), wire.Param),
            _ => new UhpUnknownEvent(wire.Type, wire.SequenceNumber, value.Clone())
        };

        return result with { Raw = value.Clone() };

        long SummaryIndex() => RequiredIndex(wire.SummaryIndex, UhpJsonFields.SummaryIndex);

        long ContentIndex() => RequiredIndex(wire.ContentIndex, UhpJsonFields.ContentIndex);

        long OutputIndex() => RequiredIndex(wire.OutputIndex, UhpJsonFields.OutputIndex);

        string ItemId() => RequiredText(wire.ItemId, UhpJsonFields.ItemId);

        UhpOutputItem Item() => new(RequiredObject(value, UhpJsonFields.Item));

        UhpResponse Response() => UhpJson.ParseResponse(RequiredObject(value, UhpJsonFields.Response));
    }

    public static JsonElement Serialize(UhpEvent update) => JsonSerializer.SerializeToElement(update, UhpJsonContext.Default.UhpEvent);

    public static void Write(Utf8JsonWriter writer, UhpEvent update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (update is UhpUnknownEvent unknown)
        {
            var data = unknown.Data;

            if (data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty(UhpJsonFields.Type, out var type) || !type.ValueEquals(unknown.Type) ||
                !data.TryGetProperty(UhpJsonFields.SequenceNumber, out var sequence) || !sequence.TryGetInt64(out var number) || number != unknown.SequenceNumber)
            {
                throw new UhpProtocolException("Unknown event JSON disagrees with its type or sequence number.");
            }

            data.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString(UhpJsonFields.Type, update.Type);
        writer.WriteNumber(UhpJsonFields.SequenceNumber, update.SequenceNumber);

        switch (update)
        {
            case UhpResponseEvent response:
                writer.WritePropertyName(UhpJsonFields.Response);
                UhpJsonWriter.WriteResponse(writer, response.Response);
                break;
            case UhpOutputItemAdded added:
                WriteItem(writer, added.Item, added.OutputIndex);
                break;
            case UhpOutputItemDone done:
                WriteItem(writer, done.Item, done.OutputIndex);
                break;
            case UhpTextDelta delta:
                WriteReference(writer, delta.ItemId, delta.OutputIndex, delta.ContentIndex);
                writer.WriteString(UhpJsonFields.Delta, delta.Text);
                break;
            case UhpTextDone done:
                WriteReference(writer, done.ItemId, done.OutputIndex, done.ContentIndex);
                writer.WriteString(UhpJsonFields.Text, done.Text);
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
                writer.WriteString(UhpJsonFields.Delta, delta.Text);
                break;
            case UhpToolArgumentsDelta delta:
                WriteReference(writer, delta.ItemId, delta.OutputIndex);
                writer.WriteString(UhpJsonFields.Delta, delta.Delta);
                break;
            case UhpToolArgumentsDone done:
                WriteReference(writer, done.ItemId, done.OutputIndex);
                writer.WriteString(UhpJsonFields.Arguments, done.Arguments);
                break;
            case UhpAnnotationAdded annotation:
                WriteReference(writer, annotation.ItemId, annotation.OutputIndex, annotation.ContentIndex);
                writer.WriteNumber(UhpJsonFields.AnnotationIndex, annotation.AnnotationIndex);
                writer.WritePropertyName(UhpJsonFields.Annotation);
                annotation.Annotation.WriteTo(writer);
                break;
            case UhpErrorEvent error:
                writer.WriteString(UhpJsonFields.Code, error.Code);
                writer.WriteString(UhpJsonFields.Message, error.Message);
                writer.WriteString(UhpJsonFields.Param, error.Param);
                break;
            default:
                throw new ArgumentException("Unsupported event implementation.", nameof(update));
        }

        UhpJsonWriter.WriteExtensions(writer, update.Raw, WrittenFields(update));
        writer.WriteEndObject();
    }

    private static readonly string[] ResponseFields = [UhpJsonFields.Type, UhpJsonFields.SequenceNumber, UhpJsonFields.Response];
    private static readonly string[] ItemFields = [UhpJsonFields.Type, UhpJsonFields.SequenceNumber, UhpJsonFields.OutputIndex, UhpJsonFields.Item];
    private static readonly string[] DeltaFields = [UhpJsonFields.Type, UhpJsonFields.SequenceNumber, UhpJsonFields.ItemId, UhpJsonFields.OutputIndex, UhpJsonFields.ContentIndex, UhpJsonFields.Delta];
    private static readonly string[] TextFields = [UhpJsonFields.Type, UhpJsonFields.SequenceNumber, UhpJsonFields.ItemId, UhpJsonFields.OutputIndex, UhpJsonFields.ContentIndex, UhpJsonFields.Text];
    private static readonly string[] PartFields = [UhpJsonFields.Type, UhpJsonFields.SequenceNumber, UhpJsonFields.ItemId, UhpJsonFields.OutputIndex, UhpJsonFields.ContentIndex, UhpJsonFields.Part];
    private static readonly string[] SummaryPartFields = [UhpJsonFields.Type, UhpJsonFields.SequenceNumber, UhpJsonFields.ItemId, UhpJsonFields.OutputIndex, UhpJsonFields.SummaryIndex, UhpJsonFields.Part];
    private static readonly string[] SummaryDeltaFields = [UhpJsonFields.Type, UhpJsonFields.SequenceNumber, UhpJsonFields.ItemId, UhpJsonFields.OutputIndex, UhpJsonFields.SummaryIndex, UhpJsonFields.Delta];
    private static readonly string[] ArgumentsDeltaFields = [UhpJsonFields.Type, UhpJsonFields.SequenceNumber, UhpJsonFields.ItemId, UhpJsonFields.OutputIndex, UhpJsonFields.Delta];
    private static readonly string[] ArgumentsFields = [UhpJsonFields.Type, UhpJsonFields.SequenceNumber, UhpJsonFields.ItemId, UhpJsonFields.OutputIndex, UhpJsonFields.Arguments];
    private static readonly string[] AnnotationFields = [UhpJsonFields.Type, UhpJsonFields.SequenceNumber, UhpJsonFields.ItemId, UhpJsonFields.OutputIndex, UhpJsonFields.ContentIndex, UhpJsonFields.AnnotationIndex, UhpJsonFields.Annotation];
    private static readonly string[] ErrorFields = [UhpJsonFields.Type, UhpJsonFields.SequenceNumber, UhpJsonFields.Code, UhpJsonFields.Message, UhpJsonFields.Param];

    private static ReadOnlySpan<string> WrittenFields(UhpEvent update) => update switch
    {
        UhpResponseEvent => ResponseFields,
        UhpOutputItemAdded or UhpOutputItemDone => ItemFields,
        UhpTextDelta => DeltaFields,
        UhpTextDone => TextFields,
        UhpContentPartAdded or UhpContentPartDone => PartFields,
        UhpReasoningSummaryPartAdded or UhpReasoningSummaryPartDone => SummaryPartFields,
        UhpReasoningSummaryDelta => SummaryDeltaFields,
        UhpToolArgumentsDelta => ArgumentsDeltaFields,
        UhpToolArgumentsDone => ArgumentsFields,
        UhpAnnotationAdded => AnnotationFields,
        UhpErrorEvent => ErrorFields,
        _ => throw new ArgumentException("Unsupported event implementation.", nameof(update))
    };

    private static void WriteItem(Utf8JsonWriter writer, UhpOutputItem item, long outputIndex)
    {
        writer.WriteNumber(UhpJsonFields.OutputIndex, outputIndex);
        writer.WritePropertyName(UhpJsonFields.Item);
        item.Raw.WriteTo(writer);
    }

    private static void WritePart(Utf8JsonWriter writer, JsonElement part, string itemId, long outputIndex, long? contentIndex, long? summaryIndex = null)
    {
        WriteReference(writer, itemId, outputIndex, contentIndex, summaryIndex);
        writer.WritePropertyName(UhpJsonFields.Part);
        part.WriteTo(writer);
    }

    private static void WriteReference(Utf8JsonWriter writer, string itemId, long outputIndex, long? contentIndex = null, long? summaryIndex = null)
    {
        writer.WriteString(UhpJsonFields.ItemId, itemId);
        writer.WriteNumber(UhpJsonFields.OutputIndex, outputIndex);

        if (contentIndex is { } content)
        {
            writer.WriteNumber(UhpJsonFields.ContentIndex, content);
        }

        if (summaryIndex is { } summary)
        {
            writer.WriteNumber(UhpJsonFields.SummaryIndex, summary);
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
