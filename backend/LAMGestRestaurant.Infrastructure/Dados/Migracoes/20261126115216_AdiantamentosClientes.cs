using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class AdiantamentosClientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClienteId",
                table: "MovimentosCartao",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClienteId",
                table: "CartoesEvento",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCartao_ClienteId_DataUtc",
                table: "MovimentosCartao",
                columns: new[] { "ClienteId", "DataUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CartoesEvento_ClienteId",
                table: "CartoesEvento",
                column: "ClienteId");

            migrationBuilder.AddForeignKey(
                name: "FK_CartoesEvento_Clientes_ClienteId",
                table: "CartoesEvento",
                column: "ClienteId",
                principalTable: "Clientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MovimentosCartao_Clientes_ClienteId",
                table: "MovimentosCartao",
                column: "ClienteId",
                principalTable: "Clientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CartoesEvento_Clientes_ClienteId",
                table: "CartoesEvento");

            migrationBuilder.DropForeignKey(
                name: "FK_MovimentosCartao_Clientes_ClienteId",
                table: "MovimentosCartao");

            migrationBuilder.DropIndex(
                name: "IX_MovimentosCartao_ClienteId_DataUtc",
                table: "MovimentosCartao");

            migrationBuilder.DropIndex(
                name: "IX_CartoesEvento_ClienteId",
                table: "CartoesEvento");

            migrationBuilder.DropColumn(
                name: "ClienteId",
                table: "MovimentosCartao");

            migrationBuilder.DropColumn(
                name: "ClienteId",
                table: "CartoesEvento");
        }
    }
}
