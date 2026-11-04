using System.Text.Json;

namespace Uhp.Core.Serialization;

internal sealed class UhpModelCatalogJsonConverter : UhpJsonConverter<UhpModelCatalog>
{
    protected override UhpModelCatalog Parse(JsonElement value) => UhpJson.ParseModelCatalog(value);

    protected override JsonElement Serialize(UhpModelCatalog value) => UhpJson.SerializeModelCatalog(value);
}
