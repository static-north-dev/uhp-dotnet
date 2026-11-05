using System.Text.Json.Serialization;
using Wire = StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Server;

/// <summary>An explicit public projection. Never place credentials or private runtime configuration in it.</summary>
public sealed record UhpSharedSessionView(
    [property: JsonPropertyName("session")] Wire.Session Session,
    [property: JsonPropertyName("turns")] Wire.TurnItem[] Turns);
