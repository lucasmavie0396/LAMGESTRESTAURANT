using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class LojasLigadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LojasLigadas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Identificador = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Nuit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Endereco = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Cor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    AdicionadaEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LojasLigadas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LojasLigadas_Endereco",
                table: "LojasLigadas",
                column: "Endereco",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LojasLigadas_Identificador",
                table: "LojasLigadas",
                column: "Identificador",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LojasLigadas");
        }
    }
}
