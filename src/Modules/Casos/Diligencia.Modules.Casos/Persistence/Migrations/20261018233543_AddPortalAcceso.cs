using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diligencia.Modules.Casos.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class AddPortalAcceso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BloqueadoHastaUtc",
                schema: "casos",
                table: "EnlacesPortal",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FallidosConsecutivos",
                schema: "casos",
                table: "EnlacesPortal",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CodigosIngreso",
                schema: "casos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnlaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodigoHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    VenceUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IntentosFallidos = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodigosIngreso", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodigosIngreso_EnlacesPortal_EnlaceId",
                        column: x => x.EnlaceId,
                        principalSchema: "casos",
                        principalTable: "EnlacesPortal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FormulariosTercero",
                schema: "casos",
                columns: table => new
                {
                    CasoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Datos = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    GuardadoUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EnviadoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EnviadoPor = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormulariosTercero", x => x.CasoId);
                    table.ForeignKey(
                        name: "FK_FormulariosTercero_Casos_CasoId",
                        column: x => x.CasoId,
                        principalSchema: "casos",
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodigosIngreso_EnlaceId_CreatedAtUtc",
                schema: "casos",
                table: "CodigosIngreso",
                columns: new[] { "EnlaceId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodigosIngreso",
                schema: "casos");

            migrationBuilder.DropTable(
                name: "FormulariosTercero",
                schema: "casos");

            migrationBuilder.DropColumn(
                name: "BloqueadoHastaUtc",
                schema: "casos",
                table: "EnlacesPortal");

            migrationBuilder.DropColumn(
                name: "FallidosConsecutivos",
                schema: "casos",
                table: "EnlacesPortal");
        }
    }
}
