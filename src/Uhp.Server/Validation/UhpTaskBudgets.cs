namespace Uhp.Server.Validation;

internal sealed record UhpTaskBudgets(long MaxStep, long MaxOutputTokens, long TimeoutSeconds);
