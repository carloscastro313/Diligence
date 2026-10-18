using Aspire.Hosting.JavaScript;

var builder = DistributedApplication.CreateBuilder(args);

// Contraseñas generadas una vez y persistidas en los user-secrets del AppHost; nunca versionadas.
IResourceBuilder<ParameterResource> Password(string name) => builder.AddParameter(
    name,
    new GenerateParameterDefault { MinLength = 24, Lower = true, Upper = true, Numeric = true, Special = false },
    secret: true,
    persist: true);

var saPassword = Password("sql-sa-password");
var migratorPassword = Password("sql-migrator-password");
var runtimePassword = Password("sql-runtime-password");

var sql = builder.AddSqlServer("sql", saPassword);

// Local: la base sobrevive entre arranques. Los tests de humo piden una base efímera para no heredar la
// contraseña de `sa` de un volumen anterior.
if (!string.Equals(builder.Configuration["Diligencia:EphemeralDatabase"], "true", StringComparison.OrdinalIgnoreCase))
    sql.WithDataVolume().WithLifetime(ContainerLifetime.Persistent);

const string database = "diligencia";

ReferenceExpression ConnectionString(string user, IResourceBuilder<ParameterResource> password) =>
    ReferenceExpression.Create(
        $"Server=127.0.0.1,{sql.Resource.Port};Database={database};User Id={user};Password={password};TrustServerCertificate=true");

// Cada proceso recibe la cadena de su propia identidad en `ConnectionStrings:diligencia`.
var migratorConnection = ConnectionString("diligencia_migrator", migratorPassword);
var runtimeConnection = ConnectionString("diligencia_app", runtimePassword);

var migrations = builder.AddProject<Projects.Diligencia_MigrationService>("migrations")
    .WithEnvironment("ConnectionStrings__diligencia", migratorConnection)
    .WithEnvironment("ConnectionStrings__diligencia-runtime", runtimeConnection)
    .WithEnvironment("ConnectionStrings__diligencia-admin",
        ReferenceExpression.Create($"Server=127.0.0.1,{sql.Resource.Port};User Id=sa;Password={saPassword};TrustServerCertificate=true"))
    .WaitFor(sql);

var api = builder.AddProject<Projects.Diligencia_Api>("api")
    .WithEnvironment("ConnectionStrings__diligencia", runtimeConnection)
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(migrations);

builder.AddProject<Projects.Diligencia_Worker>("worker")
    .WithEnvironment("ConnectionStrings__diligencia", runtimeConnection)
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(migrations);

var backoffice = builder.AddViteApp("backoffice", "../../../apps/backoffice")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpHealthCheck("/");

var portal = builder.AddViteApp("portal", "../../../apps/portal")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpHealthCheck("/");

// Ambos `npm install` escriben el mismo node_modules del workspace; en paralelo chocan (EEXIST al crear el
// symlink de @diligencia/ui). Se serializan: el instalador del portal espera al del backoffice.
IResourceBuilder<ExecutableResource> Installer(IResourceBuilder<ViteAppResource> app) =>
    builder.CreateResourceBuilder(app.Resource.Annotations.OfType<JavaScriptPackageInstallerAnnotation>().Single().Resource);

Installer(portal).WaitForCompletion(Installer(backoffice));

builder.Build().Run();
