using System.Globalization;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Caixa;

/// <summary>Posto do próprio servidor, preparado pelo instalador.</summary>
public sealed record PostoDoServidor(int TerminalId, int Numero, string Nome, bool Criado);

/// <summary>
/// O servidor é também o primeiro posto: abre o sistema por localhost (sem IP, que só os outros postos e os tablets precisam).
/// O instalador do servidor chama isto pela base de dados (comando «posto-servidor»), sem rede nem palavra-passe.
/// Correr de novo não duplica: reutiliza o posto com esse número.
/// </summary>
public class ServicoPostoServidor(IAppDbContext db, IAuditoria auditoria, TimeProvider relogio)
{
    public const string DescricaoPadrao = "Este servidor (localhost)";

    public async Task<PostoDoServidor> GarantirAsync(int numero, CancellationToken ct = default)
    {
        if (numero is < 1 or > 999)
            throw new RegraNegocioException("O número do posto tem de ser de 1 a 999.");

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        var terminal = await db.Terminais.FirstOrDefaultAsync(t => t.Numero == numero, ct);
        var criado = terminal is null;
        if (terminal is null)
        {
            terminal = db.Terminais.Add(new Terminal { Nome = await NomeLivreAsync(numero, ct), Numero = numero, Descricao = DescricaoPadrao }).Entity;
            await db.SaveChangesAsync(ct); // Id para a auditoria
            auditoria.Registar("terminal.criar", "Terminal", terminal.Id, new { terminal.Nome, terminal.Numero, DoServidor = true });
        }
        else if (!terminal.Activo)
        {
            terminal.Activo = true;
            auditoria.Registar("terminal.activar", "Terminal", terminal.Id, new { terminal.Nome, DoServidor = true });
        }

        var valor = terminal.Id.ToString(CultureInfo.InvariantCulture);
        var config = await db.Configuracoes.FirstOrDefaultAsync(c => c.Chave == ChavesConfiguracao.PostoServidor, ct)
                     ?? db.Configuracoes.Add(new Configuracao { Chave = ChavesConfiguracao.PostoServidor, Descricao = "Posto do próprio servidor (instalador)" }).Entity;
        if (config.Valor != valor)
        {
            auditoria.Registar("posto.servidor", "Terminal", terminal.Id, new { terminal.Nome, terminal.Numero, Anterior = config.Valor.Length == 0 ? null : config.Valor });
            config.Valor = valor;
            config.AlteradoEmUtc = relogio.GetUtcNow().UtcDateTime;
        }
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return new PostoDoServidor(terminal.Id, numero, terminal.Nome, criado);
    }

    /// <summary>Id do terminal do servidor (null se o instalador ainda não o escolheu).</summary>
    public static async Task<int?> TerminalAsync(IAppDbContext db, CancellationToken ct = default) =>
        int.TryParse(await db.Configuracoes.AsNoTracking().Where(c => c.Chave == ChavesConfiguracao.PostoServidor).Select(c => c.Valor).FirstOrDefaultAsync(ct),
            NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    /// <summary>«Posto 1 — Servidor»; se o nome já estiver em uso noutro terminal, acrescenta (2), (3)…</summary>
    private async Task<string> NomeLivreAsync(int numero, CancellationToken ct)
    {
        var base_ = $"Posto {numero} — Servidor";
        var nome = base_;
        for (var i = 2; await db.Terminais.AnyAsync(t => t.Nome == nome, ct); i++)
            nome = $"{base_} ({i})";
        return nome;
    }
}
