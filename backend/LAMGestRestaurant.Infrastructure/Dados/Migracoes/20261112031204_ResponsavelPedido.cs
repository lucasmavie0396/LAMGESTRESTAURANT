using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class ResponsavelPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UtilizadorResponsavelId",
                table: "Pedidos",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_UtilizadorResponsavelId",
                table: "Pedidos",
                column: "UtilizadorResponsavelId");

            migrationBuilder.AddForeignKey(
                name: "FK_Pedidos_Utilizadores_UtilizadorResponsavelId",
                table: "Pedidos",
                column: "UtilizadorResponsavelId",
                principalTable: "Utilizadores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Pedidos já existentes: o responsável é quem os criou.
            migrationBuilder.Sql("UPDATE Pedidos SET UtilizadorResponsavelId = CriadoPorId WHERE UtilizadorResponsavelId IS NULL AND CriadoPorId IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pedidos_Utilizadores_UtilizadorResponsavelId",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_UtilizadorResponsavelId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "UtilizadorResponsavelId",
                table: "Pedidos");
        }
    }
}
