using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class AbrirFecharDia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiaOperacaoId",
                table: "SessoesCaixa",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DiasOperacao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UtilizadorAberturaId = table.Column<int>(type: "int", nullable: false),
                    AbertoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ObservacoesAbertura = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UtilizadorFechoId = table.Column<int>(type: "int", nullable: true),
                    FechadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ObservacoesFecho = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Numero = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Resumo = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CriadoPorId = table.Column<int>(type: "int", nullable: true),
                    AlteradoEmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlteradoPorId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiasOperacao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiasOperacao_Utilizadores_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiasOperacao_Utilizadores_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiasOperacao_Utilizadores_UtilizadorAberturaId",
                        column: x => x.UtilizadorAberturaId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiasOperacao_Utilizadores_UtilizadorFechoId",
                        column: x => x.UtilizadorFechoId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessoesCaixa_DiaOperacaoId",
                table: "SessoesCaixa",
                column: "DiaOperacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_DiasOperacao_AlteradoPorId",
                table: "DiasOperacao",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_DiasOperacao_CriadoPorId",
                table: "DiasOperacao",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_DiasOperacao_Data",
                table: "DiasOperacao",
                column: "Data",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiasOperacao_Numero",
                table: "DiasOperacao",
                column: "Numero",
                unique: true,
                filter: "[Numero] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DiasOperacao_UtilizadorAberturaId",
                table: "DiasOperacao",
                column: "UtilizadorAberturaId");

            migrationBuilder.CreateIndex(
                name: "IX_DiasOperacao_UtilizadorFechoId",
                table: "DiasOperacao",
                column: "UtilizadorFechoId");

            migrationBuilder.CreateIndex(
                name: "UX_DiasOperacao_Aberto",
                table: "DiasOperacao",
                column: "Estado",
                unique: true,
                filter: "[Estado] = 'Aberto'");

            migrationBuilder.AddForeignKey(
                name: "FK_SessoesCaixa_DiasOperacao_DiaOperacaoId",
                table: "SessoesCaixa",
                column: "DiaOperacaoId",
                principalTable: "DiasOperacao",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SessoesCaixa_DiasOperacao_DiaOperacaoId",
                table: "SessoesCaixa");

            migrationBuilder.DropTable(
                name: "DiasOperacao");

            migrationBuilder.DropIndex(
                name: "IX_SessoesCaixa_DiaOperacaoId",
                table: "SessoesCaixa");

            migrationBuilder.DropColumn(
                name: "DiaOperacaoId",
                table: "SessoesCaixa");
        }
    }
}
