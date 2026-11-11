using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class DescargasCartoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DescargaId",
                table: "MovimentosCartao",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DescargasCartoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DataUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UtilizadorId = table.Column<int>(type: "int", nullable: false),
                    DevolveuDinheiro = table.Column<bool>(type: "bit", nullable: false),
                    MetodoPagamentoId = table.Column<int>(type: "int", nullable: true),
                    SessaoCaixaId = table.Column<int>(type: "int", nullable: true),
                    Referencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Motivo = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NumeroCartoes = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DescargasCartoes", x => x.Id);
                    table.CheckConstraint("CK_DescargasCartoes_Total", "[Total] > 0");
                    table.ForeignKey(
                        name: "FK_DescargasCartoes_MetodosPagamento_MetodoPagamentoId",
                        column: x => x.MetodoPagamentoId,
                        principalTable: "MetodosPagamento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DescargasCartoes_Utilizadores_UtilizadorId",
                        column: x => x.UtilizadorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCartao_DescargaId",
                table: "MovimentosCartao",
                column: "DescargaId");

            migrationBuilder.CreateIndex(
                name: "IX_DescargasCartoes_DataUtc",
                table: "DescargasCartoes",
                column: "DataUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DescargasCartoes_IdempotencyKey",
                table: "DescargasCartoes",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DescargasCartoes_MetodoPagamentoId",
                table: "DescargasCartoes",
                column: "MetodoPagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_DescargasCartoes_UtilizadorId",
                table: "DescargasCartoes",
                column: "UtilizadorId");

            migrationBuilder.AddForeignKey(
                name: "FK_MovimentosCartao_DescargasCartoes_DescargaId",
                table: "MovimentosCartao",
                column: "DescargaId",
                principalTable: "DescargasCartoes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MovimentosCartao_DescargasCartoes_DescargaId",
                table: "MovimentosCartao");

            migrationBuilder.DropTable(
                name: "DescargasCartoes");

            migrationBuilder.DropIndex(
                name: "IX_MovimentosCartao_DescargaId",
                table: "MovimentosCartao");

            migrationBuilder.DropColumn(
                name: "DescargaId",
                table: "MovimentosCartao");
        }
    }
}
