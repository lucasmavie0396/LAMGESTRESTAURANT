using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class ImpressorasPostos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgenteImpressoras",
                table: "Terminais",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgenteTokenHash",
                table: "Terminais",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgenteVersao",
                table: "Terminais",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AgenteVistoEmUtc",
                table: "Terminais",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PostoId",
                table: "Impressoras",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrabalhosImpressaoPosto",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TerminalId = table.Column<int>(type: "int", nullable: false),
                    ImpressoraId = table.Column<int>(type: "int", nullable: false),
                    NomeWindows = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    LarguraPapelMm = table.Column<int>(type: "int", nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Documento = table.Column<string>(type: "nvarchar(max)", maxLength: 1000000, nullable: false),
                    ComLogotipo = table.Column<bool>(type: "bit", nullable: false),
                    UtilizadorId = table.Column<int>(type: "int", nullable: true),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EntregueEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConcluidoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Erro = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrabalhosImpressaoPosto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrabalhosImpressaoPosto_Terminais_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrabalhosImpressaoPosto_Utilizadores_UtilizadorId",
                        column: x => x.UtilizadorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Impressoras_PostoId",
                table: "Impressoras",
                column: "PostoId");

            migrationBuilder.CreateIndex(
                name: "IX_TrabalhosImpressaoPosto_TerminalId_ConcluidoEmUtc",
                table: "TrabalhosImpressaoPosto",
                columns: new[] { "TerminalId", "ConcluidoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TrabalhosImpressaoPosto_UtilizadorId",
                table: "TrabalhosImpressaoPosto",
                column: "UtilizadorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Impressoras_Terminais_PostoId",
                table: "Impressoras",
                column: "PostoId",
                principalTable: "Terminais",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Impressoras_Terminais_PostoId",
                table: "Impressoras");

            migrationBuilder.DropTable(
                name: "TrabalhosImpressaoPosto");

            migrationBuilder.DropIndex(
                name: "IX_Impressoras_PostoId",
                table: "Impressoras");

            migrationBuilder.DropColumn(
                name: "AgenteImpressoras",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "AgenteTokenHash",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "AgenteVersao",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "AgenteVistoEmUtc",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "PostoId",
                table: "Impressoras");
        }
    }
}
