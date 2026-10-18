namespace Diligencia.IntegrationTests.Support;

/// <summary>La API contra el SQL Server de la fixture, con el login de runtime (el que usa en producción).</summary>
public class DbApiFactory(string connectionString) : ApiFactory
{
    protected override string? ConnectionString => connectionString;
}
