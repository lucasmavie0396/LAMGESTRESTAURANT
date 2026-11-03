using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class ModoImpressora : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Modo",
                table: "Impressoras",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                // Só aditiva: as impressoras existentes continuam a imprimir directo.
                defaultValue: "Directo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Modo",
                table: "Impressoras");
        }
    }
}
