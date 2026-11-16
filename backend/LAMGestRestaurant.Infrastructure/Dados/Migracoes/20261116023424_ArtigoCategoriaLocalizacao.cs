using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class ArtigoCategoriaLocalizacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoriaId",
                table: "Artigos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Localizacao",
                table: "Artigos",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Artigos_CategoriaId",
                table: "Artigos",
                column: "CategoriaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Artigos_Categorias_CategoriaId",
                table: "Artigos",
                column: "CategoriaId",
                principalTable: "Categorias",
                principalColumn: "Id");

            // Artigos dos produtos (ficha técnica de um só artigo): herdam a categoria do produto, quando só um produto os usa.
            migrationBuilder.Sql(@"
UPDATE a SET a.CategoriaId = x.CategoriaId
FROM Artigos a
JOIN (
    SELECT ri.ArtigoId, MIN(p.CategoriaId) AS CategoriaId
    FROM ReceitaItens ri
    JOIN Receitas r ON r.Id = ri.ReceitaId AND r.Activa = 1
    JOIN Produtos p ON p.Id = r.ProdutoId
    WHERE (SELECT COUNT(*) FROM ReceitaItens i2 WHERE i2.ReceitaId = r.Id) = 1
    GROUP BY ri.ArtigoId
    HAVING COUNT(DISTINCT p.Id) = 1
) x ON x.ArtigoId = a.Id
WHERE a.CategoriaId IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Artigos_Categorias_CategoriaId",
                table: "Artigos");

            migrationBuilder.DropIndex(
                name: "IX_Artigos_CategoriaId",
                table: "Artigos");

            migrationBuilder.DropColumn(
                name: "CategoriaId",
                table: "Artigos");

            migrationBuilder.DropColumn(
                name: "Localizacao",
                table: "Artigos");
        }
    }
}
