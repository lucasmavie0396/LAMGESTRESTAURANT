using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class ArmazemDoArtigo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ArmazemId",
                table: "Artigos",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Artigos_ArmazemId",
                table: "Artigos",
                column: "ArmazemId");

            migrationBuilder.AddForeignKey(
                name: "FK_Artigos_Armazens_ArmazemId",
                table: "Artigos",
                column: "ArmazemId",
                principalTable: "Armazens",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Artigos_Armazens_ArmazemId",
                table: "Artigos");

            migrationBuilder.DropIndex(
                name: "IX_Artigos_ArmazemId",
                table: "Artigos");

            migrationBuilder.DropColumn(
                name: "ArmazemId",
                table: "Artigos");
        }
    }
}
