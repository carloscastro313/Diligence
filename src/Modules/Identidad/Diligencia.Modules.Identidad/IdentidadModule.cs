using Diligencia.BuildingBlocks.Persistence;
using Diligencia.BuildingBlocks.Http;
using Diligencia.Modules.Identidad.Email;
using Diligencia.Modules.Identidad.Bootstrap;
using Diligencia.Modules.Identidad.Features.CreateArea;
using Diligencia.Modules.Identidad.Features.CreateUser;
using Diligencia.Modules.Identidad.Features.ForgotPassword;
using Diligencia.Modules.Identidad.Features.GetUser;
using Diligencia.Modules.Identidad.Features.ListAreas;
using Diligencia.Modules.Identidad.Features.ListUsers;
using Diligencia.Modules.Identidad.Features.RenameArea;
using Diligencia.Modules.Identidad.Features.ResetPassword;
using Diligencia.Modules.Identidad.Features.SetAreaStatus;
using Diligencia.Modules.Identidad.Features.SetUserStatus;
using Diligencia.Modules.Identidad.Features.UpdateUser;
using Diligencia.Modules.Identidad.Features.Login;
using Diligencia.Modules.Identidad.Features.Logout;
using Diligencia.Modules.Identidad.Features.Me;
using Diligencia.Modules.Identidad.Persistence;
using FluentValidation;
using Diligencia.Modules.Identidad.Authentication;
using Diligencia.Modules.Identidad.Authorization;
using Diligencia.Modules.Identidad.Contracts;
using System.Threading.RateLimiting;
using Diligencia.BuildingBlocks.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.Modules.Identidad;

internal static class RateLimitPolicies
{
    public const string Anonymous = "auth-anonymous";
}

public static class IdentidadModule
{
    // Validadores (singleton) y handlers (scoped) de los slices, por convención de nombre.
    private static IServiceCollection AddFeatureServices(this IServiceCollection services)
    {
        foreach (var type in typeof(IdentidadModule).Assembly.GetTypes().Where(t => t is { IsAbstract: false, IsClass: true }))
        {
            if (type.BaseType is { IsGenericType: true } b && b.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
                services.AddSingleton(typeof(IValidator<>).MakeGenericType(b.GetGenericArguments()[0]), type);
            else if (type.Name.EndsWith("Handler") && type.Namespace!.Contains(".Features."))
                services.AddScoped(type);
        }
        return services;
    }

    // No exige configuración JWT: worker y MigrationService arrancan sin ella.
    public static IServiceCollection AddIdentidadModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentidadDbContext>(configuration, IdentidadDbContext.Schema);
        services.AddOptions<IdentidadOptions>().Bind(configuration.GetSection(IdentidadOptions.Section));
        services.AddHttpContextAccessor();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<TokenIssuer>(); // lee la configuración al emitir: seguro en hosts sin JWT
        services.AddFeatureServices();
        services.AddScoped<IActorContext, HttpActorContext>();
        services.AddScoped<IPortalSignIn, PortalSignIn>();
        services.AddScoped<IPortalActor, HttpPortalActor>();
        return services;
    }

    // Solo la API.
    public static IServiceCollection AddIdentidadAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        configuration.GetRequired("Identidad:BackofficeBaseUrl");
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuditingAuthorizationResultHandler>();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // En TestServer la IP remota es nula: todos comparten la partición "unknown".
            o.AddPolicy(RateLimitPolicies.Anonymous, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = ctx.RequestServices.GetRequiredService<IOptions<IdentidadOptions>>().Value.AuthRateLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });
        return services.AddBearerSchemes(configuration).AddIdentityPolicies();
    }

    // Lo llama el MigrationService después de migrar (FR-014).
    public static Task BootstrapAdministratorAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        BootstrapAdministrator.RunAsync(services, cancellationToken);

    public static ApiSurfaces MapIdentidadEndpoints(this ApiSurfaces surfaces)
    {
        var backoffice = surfaces.Backoffice;
        LoginEndpoint.Map(backoffice);
        LogoutEndpoint.Map(backoffice);
        MeEndpoint.Map(backoffice);
        ForgotPasswordEndpoint.Map(backoffice);
        ResetPasswordEndpoint.Map(backoffice);
        ListAreasEndpoint.Map(backoffice);
        CreateAreaEndpoint.Map(backoffice);
        RenameAreaEndpoint.Map(backoffice);
        SetAreaStatusEndpoint.Map(backoffice);
        ListUsersEndpoint.Map(backoffice);
        GetUserEndpoint.Map(backoffice);
        CreateUserEndpoint.Map(backoffice);
        UpdateUserEndpoint.Map(backoffice);
        SetUserStatusEndpoint.Map(backoffice);
        return surfaces;
    }
}
