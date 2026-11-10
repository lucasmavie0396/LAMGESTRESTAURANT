using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class CartoesEvento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CartoesEvento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Numero = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Titular = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Saldo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartoesEvento", x => x.Id);
                    table.CheckConstraint("CK_CartoesEvento_Saldo", "[Saldo] >= 0");
                    table.ForeignKey(
                        name: "FK_CartoesEvento_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CartoesEvento_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovimentosCartao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CartaoEventoId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SaldoApos = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MetodoPagamentoId = table.Column<int>(type: "int", nullable: true),
                    SessaoCaixaId = table.Column<int>(type: "int", nullable: true),
                    MovimentoCaixaId = table.Column<int>(type: "int", nullable: true),
                    PagamentoId = table.Column<int>(type: "int", nullable: true),
                    PedidoId = table.Column<int>(type: "int", nullable: true),
                    Motivo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    UtilizadorId = table.Column<int>(type: "int", nullable: false),
                    DataUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentosCartao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovimentosCartao_CartoesEvento_CartaoEventoId",
                        column: x => x.CartaoEventoId,
                        principalTable: "CartoesEvento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosCartao_MetodosPagamento_MetodoPagamentoId",
                        column: x => x.MetodoPagamentoId,
                        principalTable: "MetodosPagamento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosCartao_MovimentosCaixa_MovimentoCaixaId",
                        column: x => x.MovimentoCaixaId,
                        principalTable: "MovimentosCaixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosCartao_Pagamentos_PagamentoId",
                        column: x => x.PagamentoId,
                        principalTable: "Pagamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimentosCartao_Utilizadores_UtilizadorId",
                        column: x => x.UtilizadorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CartoesEvento_AlteradoPorId",
                table: "CartoesEvento",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_CartoesEvento_CriadoPorId",
                table: "CartoesEvento",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_CartoesEvento_Numero",
                table: "CartoesEvento",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCartao_CartaoEventoId_Id",
                table: "MovimentosCartao",
                columns: new[] { "CartaoEventoId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCartao_DataUtc",
                table: "MovimentosCartao",
                column: "DataUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCartao_IdempotencyKey",
                table: "MovimentosCartao",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCartao_MetodoPagamentoId",
                table: "MovimentosCartao",
                column: "MetodoPagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCartao_MovimentoCaixaId",
                table: "MovimentosCartao",
                column: "MovimentoCaixaId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCartao_PagamentoId",
                table: "MovimentosCartao",
                column: "PagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCartao_UtilizadorId",
                table: "MovimentosCartao",
                column: "UtilizadorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MovimentosCartao");

            migrationBuilder.DropTable(
                name: "CartoesEvento");
        }
    }
}
