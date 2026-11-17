using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class VisorCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgenteEcras",
                table: "Terminais",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentePortas",
                table: "Terminais",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisorChave",
                table: "Terminais",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VisorColunas",
                table: "Terminais",
                type: "int",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<string>(
                name: "VisorEcra",
                table: "Terminais",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisorPorta",
                table: "Terminais",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisorProtocolo",
                table: "Terminais",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "EscPos");

            migrationBuilder.AddColumn<string>(
                name: "VisorTipo",
                table: "Terminais",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Nenhum");

            migrationBuilder.AddColumn<int>(
                name: "VisorVelocidade",
                table: "Terminais",
                type: "int",
                nullable: false,
                defaultValue: 9600);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgenteEcras",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "AgentePortas",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "VisorChave",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "VisorColunas",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "VisorEcra",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "VisorPorta",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "VisorProtocolo",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "VisorTipo",
                table: "Terminais");

            migrationBuilder.DropColumn(
                name: "VisorVelocidade",
                table: "Terminais");
        }
    }
}
