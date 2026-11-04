using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StaticNorth.Valiant.Endpoints.AspNetCore;
using StaticNorth.Valiant.Validation;
using Uhp.Server.Validation;
using Uhp.Server.Transport;
using Uhp.Core;

namespace Uhp.Server;

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
        services.AddOptions<ValiantEndpointOptions>();
        services.ConfigureHttpJsonOptions(json => json.SerializerOptions.TypeInfoResolverChain.Insert(0, UhpJsonContext.Default));
        services.TryAddSingleton(new UhpTaskBudgetsValidator(new ValiantValidationOptions { Mode = ValiantValidationMode.Fast }));
        return services;
    }

    public static RouteGroupBuilder MapUhp(this IEndpointRouteBuilder builder, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ServiceProvider.GetRequiredService<UhpServerOptions>();
        _ = builder.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var group = builder.MapGroup(prefix).AllowAnonymous();
        group.AddEndpointFilter(new UhpProtocolFilter());

        // Map this assembly's generated routes without remapping the host's unrelated endpoints under this prefix.
        new StaticNorth.Valiant.Endpoints.AspNetCore.Generated.ValiantEndpointRouteRegistrar().MapEndpoints(group);
        return group;
    }
}
