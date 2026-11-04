using StaticNorth.Valiant.Validation;

namespace Uhp.Server.Validation;

[ValiantValidator]
internal sealed partial class UhpTaskBudgetsValidator : ValidatorDefinition<UhpTaskBudgets>
{
    public override void Configure(ValidationContext<UhpTaskBudgets> context)
    {
        context.Property(request => request.MaxStep).GreaterThanOrEqualTo(0L);

        context.Property(request => request.MaxOutputTokens).GreaterThanOrEqualTo(0L);

        context.Property(request => request.TimeoutSeconds).GreaterThanOrEqualTo(0L);
    }
}
