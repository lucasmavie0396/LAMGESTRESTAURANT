using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Sistema;

/// <summary>Identidade de um servidor LAMGest (uma loja), pública: confirma a quem se está a ligar.</summary>
public sealed record IdentidadeLoja(string Identificador, string Nome, string? Nuit, string? Cor);

/// <summary>Uma loja ligada, como aparece no seletor.</summary>
public sealed record LojaDto(int Id, string Identificador, string Nome, string? Nuit, string Endereco, string? Cor);

/// <summary>Lojas para o seletor: esta (<see cref="Actual"/>) e as outras ligadas pela VPN.</summary>
public sealed record ListaLojas(IdentidadeLoja Actual, IReadOnlyList<LojaDto> Outras);

public sealed record NovaLoja([Required(ErrorMessage = "Indique o endereço da loja na VPN.")][StringLength(200)] string Endereco);

/// <summary>Consulta a outro servidor LAMGest pela VPN (identidade e lojas que ele conhece).</summary>
public interface IConsultaLojas
{
    Task<IdentidadeLoja> IdentidadeAsync(string endereco, CancellationToken ct);
    Task<ListaLojas> ListaAsync(string endereco, CancellationToken ct);
}

/// <summary>
/// Lojas do mesmo dono, cada uma com o seu servidor e a sua base de dados, ligadas pela VPN.
/// Ao acrescentar, o servidor da outra loja é contactado para confirmar o nome, o NUIT e o identificador (nunca a própria loja).
/// Importar copia a lista de outra loja. Os dados de cada loja (vendas, stock, documentos) ficam sempre no seu servidor.
/// </summary>
public class ServicoLojas(IAppDbContext db, ConfiguracoesSistema config, IConsultaLojas consulta, IAuditoria auditoria, TimeProvider relogio)
{
    public async Task<IdentidadeLoja> IdentidadeAsync(CancellationToken ct = default)
    {
        var nome = await config.TextoAsync(ChavesConfiguracao.NomeEstabelecimento);
        var nuit = await config.TextoAsync(ChavesConfiguracao.Nuit);
        var cor = await config.TextoAsync(ChavesConfiguracao.LojaCor);
        return new IdentidadeLoja(await config.TextoAsync(ChavesConfiguracao.LojaIdentificador),
            string.IsNullOrWhiteSpace(nome) ? "LAMGest Restaurant" : nome, string.IsNullOrWhiteSpace(nuit) ? null : nuit, string.IsNullOrWhiteSpace(cor) ? null : cor);
    }

    public async Task<ListaLojas> ListarAsync(CancellationToken ct = default) =>
        new(await IdentidadeAsync(ct), await db.LojasLigadas.AsNoTracking().OrderBy(l => l.Nome)
            .Select(l => new LojaDto(l.Id, l.Identificador, l.Nome, l.Nuit, l.Endereco, l.Cor)).ToListAsync(ct));

    public async Task<LojaDto> AcrescentarAsync(NovaLoja dados, CancellationToken ct = default)
    {
        var endereco = Normalizar(dados.Endereco);
        var outra = await consulta.IdentidadeAsync(endereco, ct);
        await GuardarAsync(outra, endereco, ct);
        return (await ListarAsync(ct)).Outras.Single(l => l.Identificador == outra.Identificador);
    }

    /// <summary>Copia a lista de outra loja: acrescenta essa loja e as que ela conhece (salvo esta e as que já existem).</summary>
    public async Task<ListaLojas> ImportarAsync(NovaLoja dados, CancellationToken ct = default)
    {
        var endereco = Normalizar(dados.Endereco);
        var lista = await consulta.ListaAsync(endereco, ct);
        var eu = await config.TextoAsync(ChavesConfiguracao.LojaIdentificador);
        if (lista.Actual.Identificador != eu && !await db.LojasLigadas.AnyAsync(l => l.Identificador == lista.Actual.Identificador, ct))
            await GuardarAsync(lista.Actual, endereco, ct);
        foreach (var l in lista.Outras.Where(l => l.Identificador != eu))
            if (!await db.LojasLigadas.AnyAsync(x => x.Identificador == l.Identificador || x.Endereco == l.Endereco, ct))
                await GuardarAsync(new IdentidadeLoja(l.Identificador, l.Nome, l.Nuit, l.Cor), l.Endereco, ct);
        return await ListarAsync(ct);
    }

    public async Task RetirarAsync(int id, CancellationToken ct = default)
    {
        var loja = await db.LojasLigadas.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NaoEncontradoException("Loja não encontrada.");
        db.LojasLigadas.Remove(loja);
        auditoria.Registar("loja.retirar", "LojaLigada", id, new { loja.Nome, loja.Endereco });
        await db.SaveChangesAsync(ct);
    }

    private async Task GuardarAsync(IdentidadeLoja outra, string endereco, CancellationToken ct)
    {
        if (outra.Identificador == await config.TextoAsync(ChavesConfiguracao.LojaIdentificador))
            throw new RegraNegocioException("Esse endereço é desta própria loja.");
        if (await db.LojasLigadas.AnyAsync(l => l.Identificador == outra.Identificador, ct))
            throw new ConflitoException($"A loja {outra.Nome} já está na lista.");
        if (await db.LojasLigadas.AnyAsync(l => l.Endereco == endereco, ct))
            throw new ConflitoException("Já existe uma loja com esse endereço.");
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        var loja = new LojaLigada
        {
            Identificador = outra.Identificador, Nome = outra.Nome, Nuit = outra.Nuit, Endereco = endereco, Cor = outra.Cor,
            AdicionadaEmUtc = relogio.GetUtcNow().UtcDateTime,
        };
        db.LojasLigadas.Add(loja);
        await db.SaveChangesAsync(ct);
        auditoria.Registar("loja.acrescentar", "LojaLigada", loja.Id, new { loja.Nome, loja.Nuit, loja.Endereco });
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
    }

    /// <summary>http(s)://servidor[:porta], sem barra nem caminho.</summary>
    public static string Normalizar(string endereco)
    {
        var e = endereco.Trim().TrimEnd('/');
        if (!e.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !e.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) e = "http://" + e;
        if (ServicoConfiguracoes.ValidarEnderecoExterno(e) is { } erro)
            throw new RegraNegocioException(erro);
        return e;
    }
}
