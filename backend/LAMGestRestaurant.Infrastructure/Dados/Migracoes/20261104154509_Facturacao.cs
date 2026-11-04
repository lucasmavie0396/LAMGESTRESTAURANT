using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Facturacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tipo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Numero = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EmitidoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UtilizadorId = table.Column<int>(type: "int", nullable: false),
                    Vencimento = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidaAte = table.Column<DateOnly>(type: "date", nullable: true),
                    EmitenteNome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmitenteNuit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    EmitenteEndereco = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmitenteTelefone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClienteId = table.Column<int>(type: "int", nullable: true),
                    ClienteNome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClienteNuit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ClienteEndereco = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PedidoId = table.Column<int>(type: "int", nullable: true),
                    DocumentoOrigemId = table.Column<int>(type: "int", nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Desconto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseTributavel = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Iva = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ValorPago = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ValorCreditado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MetodoPagamentoId = table.Column<int>(type: "int", nullable: true),
                    Referencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SessaoCaixaId = table.Column<int>(type: "int", nullable: true),
                    Motivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Observacoes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AnuladoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnuladoPorId = table.Column<int>(type: "int", nullable: true),
                    MotivoAnulacao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documentos", x => x.Id);
                    table.CheckConstraint("CK_Documentos_Valores", "[Total] >= 0 AND [ValorPago] >= 0 AND [ValorCreditado] >= 0 AND [ValorPago] + [ValorCreditado] <= [Total]");
                    table.ForeignKey(
                        name: "FK_Documentos_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Documentos_Documentos_DocumentoOrigemId",
                        column: x => x.DocumentoOrigemId,
                        principalTable: "Documentos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Documentos_MetodosPagamento_MetodoPagamentoId",
                        column: x => x.MetodoPagamentoId,
                        principalTable: "MetodosPagamento",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Documentos_Pedidos_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "Pedidos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Documentos_SessoesCaixa_SessaoCaixaId",
                        column: x => x.SessaoCaixaId,
                        principalTable: "SessoesCaixa",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Documentos_Utilizadores_AnuladoPorId",
                        column: x => x.AnuladoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documentos_Utilizadores_UtilizadorId",
                        column: x => x.UtilizadorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentoLinhas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentoId = table.Column<int>(type: "int", nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    ProdutoId = table.Column<int>(type: "int", nullable: true),
                    Codigo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Descricao = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PrecoUnitario = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Desconto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PercentagemIva = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MotivoIsencao = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Base = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Iva = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LinhaOrigemId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentoLinhas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentoLinhas_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LiquidacoesFactura",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReciboId = table.Column<int>(type: "int", nullable: false),
                    FacturaId = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiquidacoesFactura", x => x.Id);
                    table.CheckConstraint("CK_LiquidacoesFactura_Valor", "[Valor] > 0");
                    table.ForeignKey(
                        name: "FK_LiquidacoesFactura_Documentos_FacturaId",
                        column: x => x.FacturaId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiquidacoesFactura_Documentos_ReciboId",
                        column: x => x.ReciboId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentoLinhas_DocumentoId_Ordem",
                table: "DocumentoLinhas",
                columns: new[] { "DocumentoId", "Ordem" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentoLinhas_LinhaOrigemId",
                table: "DocumentoLinhas",
                column: "LinhaOrigemId");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_AnuladoPorId",
                table: "Documentos",
                column: "AnuladoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_ClienteId_Tipo_Estado",
                table: "Documentos",
                columns: new[] { "ClienteId", "Tipo", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_DocumentoOrigemId",
                table: "Documentos",
                column: "DocumentoOrigemId");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_IdempotencyKey",
                table: "Documentos",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_MetodoPagamentoId",
                table: "Documentos",
                column: "MetodoPagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_Numero",
                table: "Documentos",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_PedidoId",
                table: "Documentos",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_SessaoCaixaId",
                table: "Documentos",
                column: "SessaoCaixaId");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_Tipo_EmitidoEmUtc",
                table: "Documentos",
                columns: new[] { "Tipo", "EmitidoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_UtilizadorId",
                table: "Documentos",
                column: "UtilizadorId");

            migrationBuilder.CreateIndex(
                name: "IX_LiquidacoesFactura_FacturaId",
                table: "LiquidacoesFactura",
                column: "FacturaId");

            migrationBuilder.CreateIndex(
                name: "IX_LiquidacoesFactura_ReciboId_FacturaId",
                table: "LiquidacoesFactura",
                columns: new[] { "ReciboId", "FacturaId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentoLinhas");

            migrationBuilder.DropTable(
                name: "LiquidacoesFactura");

            migrationBuilder.DropTable(
                name: "Documentos");
        }
    }
}
