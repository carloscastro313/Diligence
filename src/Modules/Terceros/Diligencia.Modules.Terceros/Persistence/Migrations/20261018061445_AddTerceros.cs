using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diligencia.Modules.Terceros.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class AddTerceros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "terceros");

            migrationBuilder.CreateTable(
                name: "Terceros",
                schema: "terceros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Cuit = table.Column<string>(type: "char(11)", nullable: false),
                    TipoTercero = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TipoPersona = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RazonSocial = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terceros", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Terceros_Cuit",
                schema: "terceros",
                table: "Terceros",
                column: "Cuit",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Terceros",
                schema: "terceros");
        }
    }
}
