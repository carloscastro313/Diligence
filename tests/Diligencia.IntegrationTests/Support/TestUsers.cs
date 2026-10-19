using System.Security.Claims;
using Diligencia.Modules.Identidad.Authentication;
using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Diligencia.IntegrationTests.Support;

public sealed record TestUser(Guid Id, string Email, string? Password, Guid SessionId, string Token);

// Claves y emisores de test, distintos por superficie (FR-004), y usuarios reales con sesión en la base.
public static class TestUsers
{
    public const string DefaultPassword = "Contrasena-de-prueba-1";

    public sealed record SchemeSettings(string Issuer, string Audience, byte[] Key);

    public static SchemeSettings For(ActorSurface surface) => surface switch
    {
        ActorSurface.Backoffice => new("test-backoffice", "test-backoffice-aud", Enumerable.Repeat((byte)0xB0, 32).ToArray()),
        ActorSurface.Portal => new("test-portal", "test-portal-aud", Enumerable.Repeat((byte)0xF0, 32).ToArray()),
        _ => throw new ArgumentOutOfRangeException(nameof(surface)),
    };

    /// <summary>Inserta una sesión de portal real (el esquema Portal la exige en la base) y devuelve su token.</summary>
    public static async Task<string> CreatePortalTokenAsync(
        string connectionString, TimeProvider clock, Guid? casoId = null, Guid? terceroId = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var db = new IdentidadDbContext(
            new DbContextOptionsBuilder<IdentidadDbContext>().UseSqlServer(connectionString).Options);
        var session = new PortalSession
        {
            CasoId = casoId ?? Guid.NewGuid(), TerceroId = terceroId ?? Guid.NewGuid(),
            CreatedAtUtc = now, LastActivityUtc = now, ExpiresAtUtc = now.AddHours(8),
        };
        db.PortalSessions.Add(session);
        await db.SaveChangesAsync();
        return Sign(ActorSurface.Portal,
            [new Claim("sub", $"tercero:{session.TerceroId}"), new Claim("sid", session.Id.ToString())], session.ExpiresAtUtc);
    }

    public static string Sign(ActorSurface surface, IEnumerable<Claim> claims, DateTime expiresUtc)
    {
        var s = For(surface);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = s.Issuer,
            Audience = s.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = expiresUtc,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(s.Key), SecurityAlgorithms.HmacSha256),
        });
    }

    public static string BackofficeToken(Guid userId, Guid sessionId, DateTime expiresUtc) =>
        Sign(ActorSurface.Backoffice, [new Claim("sub", userId.ToString()), new Claim("sid", sessionId.ToString())], expiresUtc);

    /// <summary>Inserta área + usuario + roles (+ sesión) en la base y devuelve el token de esa sesión.</summary>
    public static async Task<TestUser> CreateAsync(
        string connectionString, TimeProvider clock, string[] roles,
        string? password = null, bool active = true, string? name = null, string? email = null, Guid? areaId = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var db = new IdentidadDbContext(
            new DbContextOptionsBuilder<IdentidadDbContext>().UseSqlServer(connectionString).Options);

        if (areaId is null)
        {
            var area = new Area { Name = "Area " + Guid.NewGuid().ToString("N"), CreatedAtUtc = now, UpdatedAtUtc = now };
            db.Areas.Add(area);
            areaId = area.Id;
        }

        var user = new User
        {
            Name = name ?? "Usuario de prueba",
            Email = email ?? $"u{Guid.NewGuid():N}@test.local",
            AreaId = areaId.Value,
            IsActive = active,
            PasswordHash = password is null ? null : PasswordHashing.Hash(password),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Roles = [.. roles.Select(r => new UserRole { Role = r })],
        };
        var session = new Session
        {
            UserId = user.Id, CreatedAtUtc = now, LastActivityUtc = now, ExpiresAtUtc = now.AddHours(8),
        };
        db.Users.Add(user);
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        return new TestUser(user.Id, user.Email, password, session.Id,
            BackofficeToken(user.Id, session.Id, session.ExpiresAtUtc));
    }
}
