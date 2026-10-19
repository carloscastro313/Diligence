using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diligencia.Modules.Identidad.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class AddPortalSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortalSessions",
                schema: "identidad",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CasoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TerceroId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    LastActivityUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortalSessions_CasoId",
                schema: "identidad",
                table: "PortalSessions",
                column: "CasoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortalSessions",
                schema: "identidad");
        }
    }
}
