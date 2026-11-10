using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class PostosPresencaTablets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LigacaoTablets",
                table: "Terminais",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TabletsCodigoGeradoEmUtc",
                table: "Terminais",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TabletsCodigoHash",
                table: "Terminais",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VistoEmUtc",
                table: "Terminais",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VistoEndereco",
                table: "Terminais",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VistoUtilizadorId",
                table: "Terminais",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Terminais_LigacaoTablets",
                table: "Terminais",
                column: "LigacaoTablets",
                unique: true,
                filter: "[LigacaoTablets] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Terminais_LigacaoTablets",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "LigacaoTablets",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "TabletsCodigoGeradoEmUtc",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "TabletsCodigoHash",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "VistoEmUtc",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "VistoEndereco",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "VistoUtilizadorId",
                table: "Terminais");
        }
    }
}
