using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class NumeroPosto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Numero",
                table: "Terminais",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Terminais_Numero",
                table: "Terminais",
                column: "Numero",
                unique: true,
                filter: "[Numero] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Terminais_Numero",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "Numero",
                table: "Terminais");
        }
    }
}
