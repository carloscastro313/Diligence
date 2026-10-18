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

// Clave HMAC de las sesiones del backoffice: 64 caracteres alfanuméricos (base64 válido, 48 bytes), persistida como las demás.
var backofficeSigningKey = builder.AddParameter(
    "backoffice-signing-key",
    new GenerateParameterDefault { MinLength = 64, Lower = true, Upper = true, Numeric = true, Special = false },
    secret: true,
    persist: true);

// Buzón de pruebas para los correos de invitación y recuperación (UI web en el puerto 8025).
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit")
    .WithEndpoint(targetPort: 1025, name: "smtp")
    .WithHttpEndpoint(targetPort: 8025, name: "ui");
var smtp = mailpit.GetEndpoint("smtp");

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
    .WithEnvironment("Identidad__BootstrapAdmin__Email", "admin@diligencia.local")
    .WithEnvironment("ConnectionStrings__diligencia-admin",
        ReferenceExpression.Create($"Server=127.0.0.1,{sql.Resource.Port};User Id=sa;Password={saPassword};TrustServerCertificate=true"))
    .WaitFor(sql);

var api = builder.AddProject<Projects.Diligencia_Api>("api")
    .WithEnvironment("ConnectionStrings__diligencia", runtimeConnection)
    .WithEnvironment("Authentication__Schemes__Backoffice__SigningKeys__0__Value", backofficeSigningKey)
    .WithEnvironment("Email__Smtp__Host", smtp.Property(EndpointProperty.Host))
    .WithEnvironment("Email__Smtp__Port", smtp.Property(EndpointProperty.Port))
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(migrations);

var worker = builder.AddProject<Projects.Diligencia_Worker>("worker")
    .WithEnvironment("ConnectionStrings__diligencia", runtimeConnection)
    .WithEnvironment("Email__Smtp__Host", smtp.Property(EndpointProperty.Host))
    .WithEnvironment("Email__Smtp__Port", smtp.Property(EndpointProperty.Port))
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(migrations);

var backoffice = builder.AddViteApp("backoffice", "../../../apps/backoffice")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpHealthCheck("/");

// Los enlaces de los correos apuntan al backoffice.
api.WithEnvironment("Identidad__BackofficeBaseUrl", backoffice.GetEndpoint("http"));

var portal = builder.AddViteApp("portal", "../../../apps/portal")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpHealthCheck("/");

// El worker arma el enlace de activación de casos hacia el portal.
worker.WithEnvironment("Casos__PortalBaseUrl", portal.GetEndpoint("http"));

// Ambos `npm install` escriben el mismo node_modules del workspace; en paralelo chocan (EEXIST al crear el
// symlink de @diligencia/ui). Se serializan: el instalador del portal espera al del backoffice.
IResourceBuilder<ExecutableResource> Installer(IResourceBuilder<ViteAppResource> app) =>
    builder.CreateResourceBuilder(app.Resource.Annotations.OfType<JavaScriptPackageInstallerAnnotation>().Single().Resource);

Installer(portal).WaitForCompletion(Installer(backoffice));

builder.Build().Run();
