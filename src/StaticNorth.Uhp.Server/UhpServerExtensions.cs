using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StaticNorth.Valiant.Endpoints.AspNetCore;
using StaticNorth.Valiant.Validation;
using StaticNorth.Uhp.Server.Validation;
using StaticNorth.Uhp.Core;
using StaticNorth.Uhp.Core.Providers;

namespace StaticNorth.Uhp.Server;

public static class UhpServerExtensions
{
    public static IServiceCollection AddUhpServer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TBackend>(this IServiceCollection services, Action<UhpServerOptions> configure)
        where TBackend : class, IUhpServerBackend
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new UhpServerOptions();
        configure(options);
        services.AddSingleton(options.Snapshot());
        services.TryAddSingleton<IUhpServerBackend, TBackend>();
        services.TryAddSingleton<UhpHarnessProviderRegistry>();
        services.AddOptions<ValiantEndpointOptions>();
        services.ConfigureHttpJsonOptions(json => json.SerializerOptions.TypeInfoResolverChain.Insert(0, UhpJsonContext.Default));
        services.ConfigureHttpJsonOptions(json => json.SerializerOptions.TypeInfoResolverChain.Add(Core.Contracts.UhpContractJsonContext.Default));
        services.ConfigureHttpJsonOptions(json => json.SerializerOptions.TypeInfoResolverChain.Add(Transport.UhpServerJsonContext.Default));
        services.TryAddSingleton(new UhpTaskBudgetsValidator(new ValiantValidationOptions { Mode = ValiantValidationMode.Fast }));
        return services;
    }

    public static RouteGroupBuilder MapUhp(this IEndpointRouteBuilder builder, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = builder.ServiceProvider.GetRequiredService<UhpServerOptions>();
        UhpFeatureBackends.Validate(builder.ServiceProvider, options);
        _ = builder.ServiceProvider.GetRequiredService<IAuthenticationService>();

        var group = builder.MapGroup(prefix);
        ((IEndpointConventionBuilder)group).Add(Transport.UhpBasePathMetadata.Attach);

        // Map this assembly's generated routes without remapping the host's unrelated endpoints under this prefix.
        new StaticNorth.Valiant.Endpoints.AspNetCore.Generated.ValiantEndpointRouteRegistrar().MapEndpoints(group);

        return group;
    }
}
