using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class PinEcraTactil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EcraTactil",
                table: "Utilizadores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PinHash",
                table: "Utilizadores",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EcraTactil",
                table: "Utilizadores");

            migrationBuilder.DropColumn(
                name: "PinHash",
                table: "Utilizadores");
        }
    }
}
