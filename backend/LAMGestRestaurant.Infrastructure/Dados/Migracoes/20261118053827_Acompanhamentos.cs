using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Acompanhamentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Acompanhamentos",
                table: "PedidoItens",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GruposAcompanhamentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Minimo = table.Column<int>(type: "int", nullable: false),
                    Maximo = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GruposAcompanhamentos", x => x.Id);
                    table.CheckConstraint("CK_GruposAcompanhamentos_Escolhas", "[Minimo] >= 0 AND [Maximo] >= 1 AND [Maximo] >= [Minimo]");
                    table.ForeignKey(
                        name: "FK_GruposAcompanhamentos_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GruposAcompanhamentos_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpcoesAcompanhamento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrupoId = table.Column<int>(type: "int", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpcoesAcompanhamento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpcoesAcompanhamento_GruposAcompanhamentos_GrupoId",
                        column: x => x.GrupoId,
                        principalTable: "GruposAcompanhamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProdutosGruposAcompanhamentos",
                columns: table => new
                {
                    ProdutoId = table.Column<int>(type: "int", nullable: false),
                    GrupoId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProdutosGruposAcompanhamentos", x => new { x.ProdutoId, x.GrupoId });
                    table.ForeignKey(
                        name: "FK_ProdutosGruposAcompanhamentos_GruposAcompanhamentos_GrupoId",
                        column: x => x.GrupoId,
                        principalTable: "GruposAcompanhamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProdutosGruposAcompanhamentos_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpcaoAcompanhamentoItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OpcaoId = table.Column<int>(type: "int", nullable: false),
                    ArtigoId = table.Column<int>(type: "int", nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnidadeMedidaId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpcaoAcompanhamentoItem", x => x.Id);
                    table.CheckConstraint("CK_OpcoesAcompanhamentoItens_Quantidade", "[Quantidade] > 0");
                    table.ForeignKey(
                        name: "FK_OpcaoAcompanhamentoItem_Artigos_ArtigoId",
                        column: x => x.ArtigoId,
                        principalTable: "Artigos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpcaoAcompanhamentoItem_OpcoesAcompanhamento_OpcaoId",
                        column: x => x.OpcaoId,
                        principalTable: "OpcoesAcompanhamento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpcaoAcompanhamentoItem_UnidadesMedida_UnidadeMedidaId",
                        column: x => x.UnidadeMedidaId,
                        principalTable: "UnidadesMedida",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PedidoItensAcompanhamentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PedidoItemId = table.Column<int>(type: "int", nullable: false),
                    OpcaoAcompanhamentoId = table.Column<int>(type: "int", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PedidoItensAcompanhamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PedidoItensAcompanhamentos_OpcoesAcompanhamento_OpcaoAcompanhamentoId",
                        column: x => x.OpcaoAcompanhamentoId,
                        principalTable: "OpcoesAcompanhamento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PedidoItensAcompanhamentos_PedidoItens_PedidoItemId",
                        column: x => x.PedidoItemId,
                        principalTable: "PedidoItens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GruposAcompanhamentos_AlteradoPorId",
                table: "GruposAcompanhamentos",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_GruposAcompanhamentos_CriadoPorId",
                table: "GruposAcompanhamentos",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_GruposAcompanhamentos_Nome",
                table: "GruposAcompanhamentos",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpcaoAcompanhamentoItem_ArtigoId",
                table: "OpcaoAcompanhamentoItem",
                column: "ArtigoId");

            migrationBuilder.CreateIndex(
                name: "IX_OpcaoAcompanhamentoItem_OpcaoId_ArtigoId",
                table: "OpcaoAcompanhamentoItem",
                columns: new[] { "OpcaoId", "ArtigoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpcaoAcompanhamentoItem_UnidadeMedidaId",
                table: "OpcaoAcompanhamentoItem",
                column: "UnidadeMedidaId");

            migrationBuilder.CreateIndex(
                name: "IX_OpcoesAcompanhamento_GrupoId",
                table: "OpcoesAcompanhamento",
                column: "GrupoId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidoItensAcompanhamentos_OpcaoAcompanhamentoId",
                table: "PedidoItensAcompanhamentos",
                column: "OpcaoAcompanhamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidoItensAcompanhamentos_PedidoItemId_OpcaoAcompanhamentoId",
                table: "PedidoItensAcompanhamentos",
                columns: new[] { "PedidoItemId", "OpcaoAcompanhamentoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProdutosGruposAcompanhamentos_GrupoId",
                table: "ProdutosGruposAcompanhamentos",
                column: "GrupoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpcaoAcompanhamentoItem");

            migrationBuilder.DropTable(
                name: "PedidoItensAcompanhamentos");

            migrationBuilder.DropTable(
                name: "ProdutosGruposAcompanhamentos");

            migrationBuilder.DropTable(
                name: "OpcoesAcompanhamento");

            migrationBuilder.DropTable(
                name: "GruposAcompanhamentos");

            migrationBuilder.DropColumn(
                name: "Acompanhamentos",
                table: "PedidoItens");
        }
    }
}
