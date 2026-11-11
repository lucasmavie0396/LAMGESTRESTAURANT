using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class CortePapelGaveta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AbrirGaveta",
                table: "Impressoras",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CortarPapel",
                table: "Impressoras",
                type: "bit",
                nullable: false,
                defaultValue: true);

            // Impressoras de pagamentos que já existiam: abrem a gaveta nos pagamentos (pode desligar-se em Configurações → Impressoras).
            migrationBuilder.Sql("UPDATE Impressoras SET AbrirGaveta = 1 WHERE Funcao = 'Caixa'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbrirGaveta",
                table: "Impressoras");

            migrationBuilder.DropColumn(
                name: "CortarPapel",
                table: "Impressoras");
        }
    }
}
