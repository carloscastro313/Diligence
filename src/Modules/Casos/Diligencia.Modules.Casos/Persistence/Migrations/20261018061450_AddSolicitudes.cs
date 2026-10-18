using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diligencia.Modules.Casos.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class AddSolicitudes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "casos");

            migrationBuilder.CreateTable(
                name: "Casos",
                schema: "casos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TerceroId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Cuit = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    RazonSocial = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TipoTercero = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    EstadoDesdeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CerradoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SolicitanteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Casos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnlacesPortal",
                schema: "casos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CasoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    Destinatario = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    VenceUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Entrega = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    Intentos = table.Column<int>(type: "int", nullable: false),
                    UltimoIntentoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnlacesPortal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnlacesPortal_Casos_CasoId",
                        column: x => x.CasoId,
                        principalSchema: "casos",
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Solicitudes",
                schema: "casos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CasoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SolicitanteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EsOriginal = table.Column<bool>(type: "bit", nullable: false),
                    ContactoNombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContactoEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    ContactoTelefono = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    MontoAnualEstimado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Solicitudes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Solicitudes_Casos_CasoId",
                        column: x => x.CasoId,
                        principalSchema: "casos",
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Casos_TerceroId",
                schema: "casos",
                table: "Casos",
                column: "TerceroId",
                unique: true,
                filter: "[Estado] IN ('EsperaTercero','EnVerificacion','EnAprobacion')");

            migrationBuilder.CreateIndex(
                name: "IX_EnlacesPortal_CasoId",
                schema: "casos",
                table: "EnlacesPortal",
                column: "CasoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnlacesPortal_TokenHash",
                schema: "casos",
                table: "EnlacesPortal",
                column: "TokenHash",
                unique: true,
                filter: "[TokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Solicitudes_CasoId_SolicitanteId",
                schema: "casos",
                table: "Solicitudes",
                columns: new[] { "CasoId", "SolicitanteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Solicitudes_SolicitanteId",
                schema: "casos",
                table: "Solicitudes",
                column: "SolicitanteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnlacesPortal",
                schema: "casos");

            migrationBuilder.DropTable(
                name: "Solicitudes",
                schema: "casos");

            migrationBuilder.DropTable(
                name: "Casos",
                schema: "casos");
        }
    }
}
