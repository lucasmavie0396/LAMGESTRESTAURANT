using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class IndicesRelatorios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PedidoItens_PedidoId",
                table: "PedidoItens");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Relatorios",
                table: "Pedidos",
                columns: new[] { "Estado", "FechadoEmUtc" })
                .Annotation("SqlServer:Include", new[] { "Total", "Desconto", "Impostos", "TaxaEntrega", "Tipo", "CriadoPorId", "GarcomId", "TotalPago" });

            migrationBuilder.CreateIndex(
                name: "IX_PedidoItens_Relatorios",
                table: "PedidoItens",
                column: "PedidoId")
                .Annotation("SqlServer:Include", new[] { "ProdutoId", "Quantidade", "Total", "Cancelado", "ValorImposto", "TaxaImpostoPercentagem" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pedidos_Relatorios",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_PedidoItens_Relatorios",
                table: "PedidoItens");

            migrationBuilder.CreateIndex(
                name: "IX_PedidoItens_PedidoId",
                table: "PedidoItens",
                column: "PedidoId");
        }
    }
}
