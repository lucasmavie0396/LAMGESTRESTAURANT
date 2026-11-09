using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class ArmazensUnidadesProduto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ArmazemId",
                table: "Produtos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PedeQuantidade",
                table: "Produtos",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "UnidadeMedidaId",
                table: "Produtos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ArmazemId",
                table: "EstacoesPreparacao",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_ArmazemId",
                table: "Produtos",
                column: "ArmazemId");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_UnidadeMedidaId",
                table: "Produtos",
                column: "UnidadeMedidaId");

            migrationBuilder.CreateIndex(
                name: "IX_EstacoesPreparacao_ArmazemId",
                table: "EstacoesPreparacao",
                column: "ArmazemId");

            migrationBuilder.AddForeignKey(
                name: "FK_EstacoesPreparacao_Armazens_ArmazemId",
                table: "EstacoesPreparacao",
                column: "ArmazemId",
                principalTable: "Armazens",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Produtos_Armazens_ArmazemId",
                table: "Produtos",
                column: "ArmazemId",
                principalTable: "Armazens",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Produtos_UnidadesMedida_UnidadeMedidaId",
                table: "Produtos",
                column: "UnidadeMedidaId",
                principalTable: "UnidadesMedida",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Os produtos existentes ficam na unidade «un» (a que o stock deles já usa).
            migrationBuilder.Sql("UPDATE [Produtos] SET [UnidadeMedidaId] = (SELECT TOP 1 [Id] FROM [UnidadesMedida] WHERE [Codigo] = 'un') WHERE [UnidadeMedidaId] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EstacoesPreparacao_Armazens_ArmazemId",
                table: "EstacoesPreparacao");

            migrationBuilder.DropForeignKey(
                name: "FK_Produtos_Armazens_ArmazemId",
                table: "Produtos");

            migrationBuilder.DropForeignKey(
                name: "FK_Produtos_UnidadesMedida_UnidadeMedidaId",
                table: "Produtos");

            migrationBuilder.DropIndex(
                name: "IX_Produtos_ArmazemId",
                table: "Produtos");

            migrationBuilder.DropIndex(
                name: "IX_Produtos_UnidadeMedidaId",
                table: "Produtos");

            migrationBuilder.DropIndex(
                name: "IX_EstacoesPreparacao_ArmazemId",
                table: "EstacoesPreparacao");

            migrationBuilder.DropColumn(
                name: "ArmazemId",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "PedeQuantidade",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "UnidadeMedidaId",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "ArmazemId",
                table: "EstacoesPreparacao");
        }
    }
}
