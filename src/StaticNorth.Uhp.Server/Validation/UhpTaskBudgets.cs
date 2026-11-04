namespace StaticNorth.Uhp.Server.Validation;

internal readonly record struct UhpTaskBudgets(long MaxStep, long MaxOutputTokens, long TimeoutSeconds);
