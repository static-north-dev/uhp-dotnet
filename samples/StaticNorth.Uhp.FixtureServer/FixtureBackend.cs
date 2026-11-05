using StaticNorth.Uhp.Reference;

namespace StaticNorth.Uhp.FixtureServer;

public sealed class FixtureBackend() : ReferenceBackend(new FixtureRuntime());
