using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Licencas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Licencas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LicencaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NomeComercial = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Nuit = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false),
                    EmitidaEm = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidaAte = table.Column<DateOnly>(type: "date", nullable: false),
                    Texto = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ActivadaEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActivadaPorId = table.Column<int>(type: "int", nullable: true),
                    UltimaVerificacaoUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Licencas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Licencas_Utilizadores_ActivadaPorId",
                        column: x => x.ActivadaPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Licencas_ActivadaPorId",
                table: "Licencas",
                column: "ActivadaPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Licencas_LicencaId",
                table: "Licencas",
                column: "LicencaId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Licencas");
        }
    }
}
