using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class Balanca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BalancaFormato",
                table: "Terminais",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Oito_N_1");

            migrationBuilder.AddColumn<string>(
                name: "BalancaModo",
                table: "Terminais",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Nenhuma");

            migrationBuilder.AddColumn<string>(
                name: "BalancaPorta",
                table: "Terminais",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BalancaVelocidade",
                table: "Terminais",
                type: "int",
                nullable: false,
                defaultValue: 9600);

            migrationBuilder.AddColumn<int>(
                name: "CodigoBalanca",
                table: "Produtos",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_CodigoBalanca",
                table: "Produtos",
                column: "CodigoBalanca",
                unique: true,
                filter: "[CodigoBalanca] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Produtos_CodigoBalanca",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "BalancaFormato",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "BalancaModo",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "BalancaPorta",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "BalancaVelocidade",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "CodigoBalanca",
                table: "Produtos");
        }
    }
}
