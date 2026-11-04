namespace Uhp.Client.InteropTests;

internal sealed record InteropReport(string Implementation, string? ServerVersion, string? AdvertisedName, string? AdvertisedServerVersion, string? NegotiatedVersion, IReadOnlyDictionary<string, bool>? Capabilities, DateTimeOffset MeasuredAt, IReadOnlyDictionary<string, string> Operations, string? Reason);
