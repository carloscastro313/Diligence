using Diligencia.Modules.Identidad.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.Modules.Identidad.Authorization;

internal static class PolicySetup
{
    public static IServiceCollection AddIdentityPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(// Con esquemas explícitos: sin DefaultScheme, una ruta desconocida daría 500 en vez de 401.
            new AuthorizationPolicyBuilder(AuthSchemes.Backoffice, AuthSchemes.Portal).RequireAuthenticatedUser().Build())
            .AddPolicy(AuthPolicies.Backoffice, p => p.AddAuthenticationSchemes(AuthSchemes.Backoffice).RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.Portal, p => p.AddAuthenticationSchemes(AuthSchemes.Portal).RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.AuditReader, p => p.AddAuthenticationSchemes(AuthSchemes.Backoffice)
                .RequireAuthenticatedUser()
                .RequireRole(BackofficeRoles.Auditor, BackofficeRoles.Administrador))
            .AddPolicy(AuthPolicies.UserDirectoryReader, p => p.AddAuthenticationSchemes(AuthSchemes.Backoffice)
                .RequireAuthenticatedUser()
                .RequireRole(BackofficeRoles.Administrador, BackofficeRoles.Auditor))
            .AddPolicy(AuthPolicies.Administrator, p => p.AddAuthenticationSchemes(AuthSchemes.Backoffice)
                .RequireAuthenticatedUser()
                .RequireRole(BackofficeRoles.Administrador));
        return services;
    }
}
