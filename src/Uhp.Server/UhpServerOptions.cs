using Uhp.Core;

namespace Uhp.Server;

public sealed class UhpServerOptions
{
    public string AuthenticationScheme { get; set; } = "";
    public string? AuthorizationPolicy { get; set; }
    public string DefaultVersion { get; set; } = UhpProtocolVersions.Current;
    public IReadOnlyList<string> Versions { get; set; } = UhpProtocolVersions.Supported;
    public UhpCapabilities Capabilities { get; set; } = new(new Dictionary<string, bool>
    {
        ["streaming"] = true,
        ["sessions"] = true,
        ["cancellation"] = true,
        ["files_input"] = false,
        ["files_output"] = false,
        ["session_listing"] = false,
        ["harness_management"] = false,
        ["session_sharing"] = false,
        ["plugins"] = false,
        ["environments"] = false,
        ["memories"] = false,
        ["input_requests"] = false,
        ["input_history"] = false,
        ["idempotency"] = false
    });
    public string ImplementationName { get; set; } = "UHP .NET server";
    public string ImplementationVersion { get; set; } = "0.1.0-preview.1";
    public long MaximumRequestBodyBytes { get; set; } = 1_048_576;

    internal UhpServerOptions Snapshot()
    {
        if (string.IsNullOrWhiteSpace(AuthenticationScheme))
        {
            throw new ArgumentException("An explicitly configured ASP.NET Core authentication scheme is required.");
        }

        if (Versions.Count == 0 || !Versions.Contains(DefaultVersion, StringComparer.Ordinal) || Versions.Any(version => !UhpProtocolVersions.Supported.Contains(version, StringComparer.Ordinal)))
        {
            throw new ArgumentException("Server protocol dates must come from the SDK compatibility table and include the default.");
        }

        if (!Capabilities.Streaming || !Capabilities.Sessions || !Capabilities.Cancellation || MaximumRequestBodyBytes <= 0)
        {
            throw new ArgumentException("The preview requires streaming, sessions, cancellation, and a positive request body limit.");
        }

        if (Capabilities.Values.Any(pair => pair.Value && pair.Key is not ("streaming" or "sessions" or "cancellation" or "idempotency")))
        {
            throw new ArgumentException("The preview cannot advertise capabilities whose endpoints it does not implement.");
        }

        return new UhpServerOptions
        {
            AuthenticationScheme = AuthenticationScheme,
            AuthorizationPolicy = AuthorizationPolicy,
            DefaultVersion = DefaultVersion,
            Versions = Array.AsReadOnly(Versions.ToArray()),
            Capabilities = new UhpCapabilities(Capabilities.Values),
            ImplementationName = ImplementationName,
            ImplementationVersion = ImplementationVersion,
            MaximumRequestBodyBytes = MaximumRequestBodyBytes
        };
    }
}
