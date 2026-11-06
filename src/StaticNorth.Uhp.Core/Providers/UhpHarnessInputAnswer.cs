namespace StaticNorth.Uhp.Core.Providers;

/// <summary>An ephemeral answer delivered to a runtime, separate from retained conversation input.</summary>
public sealed record UhpHarnessInputAnswer(string RequestId, string Kind, string Value, string Prompt);
