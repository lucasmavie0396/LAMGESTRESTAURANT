using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class PagamentosCompras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PagamentosCompras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompraId = table.Column<int>(type: "int", nullable: false),
                    DataUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MetodoPagamentoId = table.Column<int>(type: "int", nullable: false),
                    Referencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SessaoCaixaId = table.Column<int>(type: "int", nullable: true),
                    UtilizadorId = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AnuladoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnuladoPorId = table.Column<int>(type: "int", nullable: true),
                    MotivoAnulacao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagamentosCompras", x => x.Id);
                    table.CheckConstraint("CK_PagamentosCompras_Valor", "[Valor] > 0");
                    table.ForeignKey(
                        name: "FK_PagamentosCompras_Compras_CompraId",
                        column: x => x.CompraId,
                        principalTable: "Compras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PagamentosCompras_MetodosPagamento_MetodoPagamentoId",
                        column: x => x.MetodoPagamentoId,
                        principalTable: "MetodosPagamento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PagamentosCompras_SessoesCaixa_SessaoCaixaId",
                        column: x => x.SessaoCaixaId,
                        principalTable: "SessoesCaixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PagamentosCompras_Utilizadores_AnuladoPorId",
                        column: x => x.AnuladoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PagamentosCompras_Utilizadores_UtilizadorId",
                        column: x => x.UtilizadorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosCompras_AnuladoPorId",
                table: "PagamentosCompras",
                column: "AnuladoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosCompras_CompraId_Estado",
                table: "PagamentosCompras",
                columns: new[] { "CompraId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosCompras_DataUtc",
                table: "PagamentosCompras",
                column: "DataUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosCompras_IdempotencyKey",
                table: "PagamentosCompras",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosCompras_MetodoPagamentoId",
                table: "PagamentosCompras",
                column: "MetodoPagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosCompras_SessaoCaixaId",
                table: "PagamentosCompras",
                column: "SessaoCaixaId");

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosCompras_UtilizadorId",
                table: "PagamentosCompras",
                column: "UtilizadorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PagamentosCompras");
        }
    }
}
