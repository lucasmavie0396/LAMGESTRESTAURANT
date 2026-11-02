using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class PedidoUnicoPorMesa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_Pedidos_MesaAberta",
                table: "Pedidos",
                column: "MesaId",
                unique: true,
                filter: "[MesaId] IS NOT NULL AND [Estado] = 'Aberto'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Pedidos_MesaAberta",
                table: "Pedidos");
        }
    }
}
