using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class DiaAnulado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DiasOperacao_Data",
                table: "DiasOperacao");

            migrationBuilder.AddColumn<DateTime>(
                name: "AnuladoEmUtc",
                table: "DiasOperacao",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoAnulacao",
                table: "DiasOperacao",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UtilizadorAnulacaoId",
                table: "DiasOperacao",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiasOperacao_Data",
                table: "DiasOperacao",
                column: "Data",
                unique: true,
                filter: "[Estado] <> 'Anulado'");

            migrationBuilder.CreateIndex(
                name: "IX_DiasOperacao_UtilizadorAnulacaoId",
                table: "DiasOperacao",
                column: "UtilizadorAnulacaoId");

            migrationBuilder.AddForeignKey(
                name: "FK_DiasOperacao_Utilizadores_UtilizadorAnulacaoId",
                table: "DiasOperacao",
                column: "UtilizadorAnulacaoId",
                principalTable: "Utilizadores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DiasOperacao_Utilizadores_UtilizadorAnulacaoId",
                table: "DiasOperacao");

            migrationBuilder.DropIndex(
                name: "IX_DiasOperacao_Data",
                table: "DiasOperacao");

            migrationBuilder.DropIndex(
                name: "IX_DiasOperacao_UtilizadorAnulacaoId",
                table: "DiasOperacao");

            migrationBuilder.DropColumn(
                name: "AnuladoEmUtc",
                table: "DiasOperacao");

            migrationBuilder.DropColumn(
                name: "MotivoAnulacao",
                table: "DiasOperacao");

            migrationBuilder.DropColumn(
                name: "UtilizadorAnulacaoId",
                table: "DiasOperacao");

            migrationBuilder.CreateIndex(
                name: "IX_DiasOperacao_Data",
                table: "DiasOperacao",
                column: "Data",
                unique: true);
        }
    }
}
