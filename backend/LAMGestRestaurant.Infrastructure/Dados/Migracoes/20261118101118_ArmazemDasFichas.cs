using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class ArmazemDasFichas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ArmazemId",
                table: "ReceitaItens",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ArmazemId",
                table: "OpcaoAcompanhamentoItem",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceitaItens_ArmazemId",
                table: "ReceitaItens",
                column: "ArmazemId");

            migrationBuilder.CreateIndex(
                name: "IX_OpcaoAcompanhamentoItem_ArmazemId",
                table: "OpcaoAcompanhamentoItem",
                column: "ArmazemId");

            migrationBuilder.AddForeignKey(
                name: "FK_OpcaoAcompanhamentoItem_Armazens_ArmazemId",
                table: "OpcaoAcompanhamentoItem",
                column: "ArmazemId",
                principalTable: "Armazens",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReceitaItens_Armazens_ArmazemId",
                table: "ReceitaItens",
                column: "ArmazemId",
                principalTable: "Armazens",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OpcaoAcompanhamentoItem_Armazens_ArmazemId",
                table: "OpcaoAcompanhamentoItem");

            migrationBuilder.DropForeignKey(
                name: "FK_ReceitaItens_Armazens_ArmazemId",
                table: "ReceitaItens");

            migrationBuilder.DropIndex(
                name: "IX_ReceitaItens_ArmazemId",
                table: "ReceitaItens");

            migrationBuilder.DropIndex(
                name: "IX_OpcaoAcompanhamentoItem_ArmazemId",
                table: "OpcaoAcompanhamentoItem");

            migrationBuilder.DropColumn(
                name: "ArmazemId",
                table: "ReceitaItens");

            migrationBuilder.DropColumn(
                name: "ArmazemId",
                table: "OpcaoAcompanhamentoItem");
        }
    }
}
