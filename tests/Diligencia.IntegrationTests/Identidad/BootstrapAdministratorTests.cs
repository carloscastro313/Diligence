using Diligencia.IntegrationTests.Support;

namespace Diligencia.IntegrationTests.Identidad;

// FR-014 / R8: el MigrationService crea el primer administrador, una sola vez.
public class BootstrapAdministratorTests(SqlServerFixture sql)
{
    [Fact]
    public async Task CreatesAreaAndPasswordlessAdministrator_OnlyWhenThereIsNone_AndAudits()
    {
        var email = $"boot{Guid.NewGuid():N}@test.local";
        var db = await sql.CreateDatabaseAsync(new()
        {
            ["Identidad:BootstrapAdmin:Email"] = $"  {email.ToUpperInvariant()} ",
            ["Identidad:BootstrapAdmin:Name"] = "Admin Inicial",
        });

        async Task<int> Count(string sqlText) => await SqlServerFixture.ScalarAsync<int>(db.Migrator, sqlText);

        Assert.Equal(1, await Count($"""
            SELECT COUNT(*) FROM identidad.Users u JOIN identidad.UserRoles r ON r.UserId = u.Id
            WHERE u.Email = '{email}' AND r.Role = 'Administrador' AND u.IsActive = 1 AND u.PasswordHash IS NULL
            """));
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM identidad.Areas WHERE Name = 'Compliance'"));

        // Idempotente: volver a correr no duplica nada, ni siquiera con otro correo configurado.
        db.Settings["Identidad:BootstrapAdmin:Email"] = "otro@test.local";
        await SqlServerFixture.MigrateAsync(db.Settings);
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM identidad.Users"));
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM identidad.Areas"));

        // Auditado con actor de sistema.
        Assert.Equal(1, await Count("""
            SELECT COUNT(*) FROM auditoria.AuditEvents
            WHERE Action = 'Created' AND EntityType = 'Usuario' AND ActorId LIKE 'system:%' AND Surface = 'System'
            """));
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM auditoria.AuditEvents WHERE Action = 'Created' AND EntityType = 'Area' AND ActorId LIKE 'system:%'"));
    }

    [Fact]
    public async Task WithoutConfiguredEmail_DoesNothing()
    {
        var db = await sql.CreateDatabaseAsync();

        Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(db.Migrator, "SELECT COUNT(*) FROM identidad.Users"));
    }
}
