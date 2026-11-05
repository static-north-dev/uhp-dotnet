namespace StaticNorth.Uhp.Reference;

/// <summary>An ephemeral answer delivered to a runtime, separate from retained conversation input.</summary>
public sealed record ReferenceInputAnswer(string RequestId, string Kind, string Value, string Prompt);
