using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class ImpressoraCategoriaProdutoEImagens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ImpressoraId",
                table: "Produtos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImpressoraId",
                table: "Categorias",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImagensProduto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TipoConteudo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Dados = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Largura = table.Column<int>(type: "int", nullable: false),
                    Altura = table.Column<int>(type: "int", nullable: false),
                    Origem = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Autor = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Licenca = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImagensProduto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImagensProduto_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_ImpressoraId",
                table: "Produtos",
                column: "ImpressoraId");

            migrationBuilder.CreateIndex(
                name: "IX_Categorias_ImpressoraId",
                table: "Categorias",
                column: "ImpressoraId");

            migrationBuilder.CreateIndex(
                name: "IX_ImagensProduto_CriadoPorId",
                table: "ImagensProduto",
                column: "CriadoPorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Categorias_Impressoras_ImpressoraId",
                table: "Categorias",
                column: "ImpressoraId",
                principalTable: "Impressoras",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Produtos_Impressoras_ImpressoraId",
                table: "Produtos",
                column: "ImpressoraId",
                principalTable: "Impressoras",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categorias_Impressoras_ImpressoraId",
                table: "Categorias");

            migrationBuilder.DropForeignKey(
                name: "FK_Produtos_Impressoras_ImpressoraId",
                table: "Produtos");

            migrationBuilder.DropTable(
                name: "ImagensProduto");

            migrationBuilder.DropIndex(
                name: "IX_Produtos_ImpressoraId",
                table: "Produtos");

            migrationBuilder.DropIndex(
                name: "IX_Categorias_ImpressoraId",
                table: "Categorias");

            migrationBuilder.DropColumn(
                name: "ImpressoraId",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "ImpressoraId",
                table: "Categorias");
        }
    }
}
