using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class TabelasPreco : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TabelaPrecoId",
                table: "Zonas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TabelaPrecoId",
                table: "Pedidos",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TabelasPreco",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TabelasPreco", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrecosProdutos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TabelaPrecoId = table.Column<int>(type: "int", nullable: false),
                    ProdutoId = table.Column<int>(type: "int", nullable: false),
                    Preco = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrecosProdutos", x => x.Id);
                    table.CheckConstraint("CK_PrecosProdutos_Preco", "[Preco] >= 0");
                    table.ForeignKey(
                        name: "FK_PrecosProdutos_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrecosProdutos_TabelasPreco_TabelaPrecoId",
                        column: x => x.TabelaPrecoId,
                        principalTable: "TabelasPreco",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Zonas_TabelaPrecoId",
                table: "Zonas",
                column: "TabelaPrecoId");

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_TabelaPrecoId",
                table: "Pedidos",
                column: "TabelaPrecoId");

            migrationBuilder.CreateIndex(
                name: "IX_PrecosProdutos_ProdutoId",
                table: "PrecosProdutos",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_PrecosProdutos_TabelaPrecoId_ProdutoId",
                table: "PrecosProdutos",
                columns: new[] { "TabelaPrecoId", "ProdutoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TabelasPreco_Nome",
                table: "TabelasPreco",
                column: "Nome",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Pedidos_TabelasPreco_TabelaPrecoId",
                table: "Pedidos",
                column: "TabelaPrecoId",
                principalTable: "TabelasPreco",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Zonas_TabelasPreco_TabelaPrecoId",
                table: "Zonas",
                column: "TabelaPrecoId",
                principalTable: "TabelasPreco",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pedidos_TabelasPreco_TabelaPrecoId",
                table: "Pedidos");

            migrationBuilder.DropForeignKey(
                name: "FK_Zonas_TabelasPreco_TabelaPrecoId",
                table: "Zonas");

            migrationBuilder.DropTable(
                name: "PrecosProdutos");

            migrationBuilder.DropTable(
                name: "TabelasPreco");

            migrationBuilder.DropIndex(
                name: "IX_Zonas_TabelaPrecoId",
                table: "Zonas");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_TabelaPrecoId",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "TabelaPrecoId",
                table: "Zonas");

            migrationBuilder.DropColumn(
                name: "TabelaPrecoId",
                table: "Pedidos");
        }
    }
}
