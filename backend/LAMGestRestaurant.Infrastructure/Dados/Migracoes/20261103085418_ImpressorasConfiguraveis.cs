using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class ImpressorasConfiguraveis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Tipo",
                table: "Impressoras",
                newName: "Funcao");

            migrationBuilder.RenameColumn(
                name: "Endereco",
                table: "Impressoras",
                newName: "NomeWindows");

            // A antiga função «Recibo» passa a chamar-se «Caixa».
            migrationBuilder.Sql("UPDATE Impressoras SET Funcao = N'Caixa' WHERE Funcao = N'Recibo'");

            migrationBuilder.AddColumn<int>(
                name: "TerminalId",
                table: "Impressoras",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Impressoras_TerminalId",
                table: "Impressoras",
                column: "TerminalId");

            migrationBuilder.AddForeignKey(
                name: "FK_Impressoras_Terminais_TerminalId",
                table: "Impressoras",
                column: "TerminalId",
                principalTable: "Terminais",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Impressoras_Terminais_TerminalId",
                table: "Impressoras");

            migrationBuilder.DropIndex(
                name: "IX_Impressoras_TerminalId",
                table: "Impressoras");

            migrationBuilder.DropColumn(
                name: "TerminalId",
                table: "Impressoras");

            migrationBuilder.RenameColumn(
                name: "NomeWindows",
                table: "Impressoras",
                newName: "Endereco");

            migrationBuilder.RenameColumn(
                name: "Funcao",
                table: "Impressoras",
                newName: "Tipo");
        }
    }
}
