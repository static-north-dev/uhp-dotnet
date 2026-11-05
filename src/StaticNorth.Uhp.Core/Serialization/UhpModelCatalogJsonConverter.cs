using System.Text.Json;

namespace StaticNorth.Uhp.Core.Serialization;

internal sealed class UhpModelCatalogJsonConverter : UhpJsonConverter<UhpModelCatalog>
{
    protected override UhpModelCatalog Parse(JsonElement value) => UhpJson.ParseModelCatalog(value);

    protected override void WriteValue(Utf8JsonWriter writer, UhpModelCatalog value) => UhpJsonWriter.WriteCatalog(writer, value);
}
