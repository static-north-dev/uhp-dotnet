using System.Text.Json;
using System.Text.Json.Serialization;

namespace Uhp.Core.Serialization;

internal abstract class UhpJsonConverter<T> : JsonConverter<T> where T : class
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Parse(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => Serialize(value).WriteTo(writer);

    protected abstract T Parse(JsonElement value);

    protected abstract JsonElement Serialize(T value);
}
