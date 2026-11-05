using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LAMGestRestaurant.Infrastructure.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class SessoesCaixaPorOperador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_SessoesCaixa_TerminalAberta",
                table: "SessoesCaixa");

            migrationBuilder.CreateIndex(
                name: "IX_SessoesCaixa_TerminalId_Estado",
                table: "SessoesCaixa",
                columns: new[] { "TerminalId", "Estado" });

            // Uma sessão aberta por utilizador. O sistema já não deixava abrir duas, mas se uma base antiga tiver um utilizador
            // com duas sessões abertas (corrida antiga), o índice fica não único em vez de impedir a actualização; nada é alterado nos dados.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [SessoesCaixa] WHERE [Estado] = 'Aberta' GROUP BY [UtilizadorAberturaId] HAVING COUNT(*) > 1)
                    CREATE UNIQUE INDEX [UX_SessoesCaixa_UtilizadorAberta] ON [SessoesCaixa] ([UtilizadorAberturaId]) WHERE [Estado] = 'Aberta';
                ELSE
                    CREATE INDEX [UX_SessoesCaixa_UtilizadorAberta] ON [SessoesCaixa] ([UtilizadorAberturaId]) WHERE [Estado] = 'Aberta';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SessoesCaixa_TerminalId_Estado",
                table: "SessoesCaixa");

            migrationBuilder.DropIndex(
                name: "UX_SessoesCaixa_UtilizadorAberta",
                table: "SessoesCaixa");

            migrationBuilder.CreateIndex(
                name: "UX_SessoesCaixa_TerminalAberta",
                table: "SessoesCaixa",
                column: "TerminalId",
                unique: true,
                filter: "[Estado] = 'Aberta'");
        }
    }
}
