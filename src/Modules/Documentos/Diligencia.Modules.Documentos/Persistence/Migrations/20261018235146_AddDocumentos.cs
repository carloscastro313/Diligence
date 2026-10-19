using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Diligencia.Modules.Documentos.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class AddDocumentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "documentos");

            migrationBuilder.CreateTable(
                name: "Configuracion",
                schema: "documentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    TamanoMaximoBytes = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configuracion", x => x.Id);
                    table.CheckConstraint("CK_Configuracion_Id", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Documentos",
                schema: "documentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CasoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TerceroId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NombreOriginal = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TamanoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CargadoPor = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CargadoUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BajaUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Baja = table.Column<string>(type: "nvarchar(32)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documentos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentoContenidos",
                schema: "documentos",
                columns: table => new
                {
                    DocumentoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nonce = table.Column<byte[]>(type: "binary(12)", nullable: false),
                    Tag = table.Column<byte[]>(type: "binary(16)", nullable: false),
                    Cifrado = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentoContenidos", x => x.DocumentoId);
                    table.ForeignKey(
                        name: "FK_DocumentoContenidos_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalSchema: "documentos",
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "documentos",
                table: "Configuracion",
                columns: new[] { "Id", "TamanoMaximoBytes", "UpdatedAtUtc" },
                values: new object[] { 1, 10485760L, new DateTime(2026, 10, 18, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_CasoId",
                schema: "documentos",
                table: "Documentos",
                column: "CasoId");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_CasoId_Tipo",
                schema: "documentos",
                table: "Documentos",
                columns: new[] { "CasoId", "Tipo" },
                unique: true,
                filter: "[BajaUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Configuracion",
                schema: "documentos");

            migrationBuilder.DropTable(
                name: "DocumentoContenidos",
                schema: "documentos");

            migrationBuilder.DropTable(
                name: "Documentos",
                schema: "documentos");
        }
    }
}
