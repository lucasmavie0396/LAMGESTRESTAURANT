using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Cartoes;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Cartoes;

/// <summary>Cartão com saldo por descarregar e o acumulado até ele.</summary>
public sealed record LinhaSaldoDescarga(int Id, string Numero, string? Titular, decimal Saldo, decimal Acumulado, bool Activo);

public sealed record SaldosDescarga(IReadOnlyList<LinhaSaldoDescarga> Cartoes, decimal Total);

/// <summary><see cref="Saldo"/>: o saldo que o operador viu; se entretanto mudou, a descarga é recusada.</summary>
public sealed record ItemDescarga(int Id, decimal Saldo);

public sealed record NovaDescarga(
    [MinLength(1, ErrorMessage = "Não há cartões para descarregar.")] IReadOnlyList<ItemDescarga> Cartoes,
    bool DevolverDinheiro,
    int? MetodoPagamentoId,
    [StringLength(100, ErrorMessage = "A referência não pode exceder 100 caracteres.")] string? Referencia,
    [Required(ErrorMessage = "Indique o motivo.")]
    [StringLength(250, MinimumLength = 3, ErrorMessage = "O motivo deve ter entre 3 e 250 caracteres.")] string Motivo);

public sealed record LinhaDescarga(string Numero, string? Titular, decimal Saldo, decimal Acumulado);

public sealed record DescargaDto(int Id, DateTime DataUtc, string? Operador, bool DevolveuDinheiro, string? Metodo, int? SessaoCaixaId, string Motivo, decimal Total,
    IReadOnlyList<LinhaDescarga> Cartoes);

public sealed partial class ServicoCartoes
{
    public const int MaximoCartoesDescarga = 5000;

    /// <summary>Saldos a descarregar: os cartões indicados ou, sem indicação, todos os que têm saldo (bloqueados incluídos).</summary>
    public async Task<SaldosDescarga> SaldosParaDescargaAsync(IReadOnlyCollection<int>? ids, CancellationToken ct)
    {
        var q = db.CartoesEvento.AsNoTracking().Where(c => c.Saldo > 0);
        if (ids is { Count: > 0 })
            q = q.Where(c => ids.Contains(c.Id));
        var cartoes = await q.OrderBy(c => c.Numero).Select(c => new { c.Id, c.Numero, c.Titular, c.Saldo, c.Activo }).ToListAsync(ct);
        var acumulado = 0m;
        return new SaldosDescarga([.. cartoes.Select(c => new LinhaSaldoDescarga(c.Id, c.Numero, c.Titular, c.Saldo, acumulado += c.Saldo, c.Activo))], acumulado);
    }

    /// <summary>
    /// Descarrega o saldo restante, tudo ou nada. Com devolução, o dinheiro sai do caixa aberto de quem descarrega pelo método escolhido;
    /// sem devolução o saldo apenas é retirado. Cada cartão fica com um movimento (só inserção) e o conjunto na auditoria.
    /// </summary>
    public async Task<(DescargaDto Descarga, bool Criada)> DescarregarAsync(NovaDescarga dados, string? chaveIdempotencia)
    {
        var chave = ServicoPedidos.ValidarChave(chaveIdempotencia);
        if (await db.DescargasCartoes.AsNoTracking().Where(d => d.IdempotencyKey == chave).Select(d => (int?)d.Id).FirstOrDefaultAsync() is int existente)
            return (await ObterDescargaAsync(existente, default), false);
        if (dados.Cartoes.Count > MaximoCartoesDescarga)
            throw new RegraNegocioException($"No máximo {MaximoCartoesDescarga} cartões de cada vez.");
        var ids = dados.Cartoes.Select(c => c.Id).Distinct().ToList();
        if (ids.Count != dados.Cartoes.Count)
            throw new RegraNegocioException("O mesmo cartão aparece mais de uma vez.");
        var utilizador = actual.Id ?? throw new RegraNegocioException("Sessão de utilizador inválida.");
        var motivo = dados.Motivo.Trim();

        int id;
        await using (var transacao = await db.Database.BeginTransactionAsync())
        {
            var cartoes = await db.CartoesEvento.Where(c => ids.Contains(c.Id)).OrderBy(c => c.Numero).ToListAsync();
            if (cartoes.Count != ids.Count)
                throw new NaoEncontradoException("Cartão não encontrado.");
            var vistos = dados.Cartoes.ToDictionary(c => c.Id, c => c.Saldo);
            foreach (var c in cartoes)
            {
                if (c.Saldo <= 0)
                    throw new ConflitoException($"O cartão {c.Numero} já não tem saldo. Actualize a lista.");
                if (c.Saldo != vistos[c.Id])
                    throw new ConflitoException($"O saldo do cartão {c.Numero} mudou para {c.Saldo:N2}. Actualize a lista.");
            }

            var total = cartoes.Sum(c => c.Saldo);
            var agora = Agora;
            var descarga = new DescargaCartoes
            {
                DataUtc = agora, UtilizadorId = utilizador, DevolveuDinheiro = dados.DevolverDinheiro, Motivo = motivo,
                Total = total, NumeroCartoes = cartoes.Count, IdempotencyKey = chave,
            };
            MovimentoCaixa? movimentoCaixa = null;
            if (dados.DevolverDinheiro)
            {
                var metodo = dados.MetodoPagamentoId is int m ? await db.MetodosPagamento.FirstOrDefaultAsync(x => x.Id == m && x.Activo) : null;
                if (metodo is null || metodo.Codigo == CodigoMetodo)
                    throw new RegraNegocioException("Escolha como o dinheiro é devolvido (dinheiro, M-Pesa…).");
                var referencia = string.IsNullOrWhiteSpace(dados.Referencia) ? null : dados.Referencia.Trim();
                if (metodo.ExigeReferencia && referencia is null)
                    throw new RegraNegocioException($"Indique a referência da transacção {metodo.Nome}.");
                var sessao = await db.SessoesCaixa.FirstOrDefaultAsync(s => s.UtilizadorAberturaId == utilizador && s.Estado == EstadoSessaoCaixa.Aberta)
                             ?? throw new RegraNegocioException("Abra o seu caixa para devolver o dinheiro dos cartões.");
                movimentoCaixa = new MovimentoCaixa
                {
                    SessaoCaixaId = sessao.Id, Tipo = TipoMovimentoCaixa.Saida, MetodoPagamentoId = metodo.Id, Valor = total,
                    Motivo = $"Descarga de {cartoes.Count} Cartão(ões) Evento: {motivo}", UtilizadorId = utilizador, DataUtc = agora,
                };
                db.MovimentosCaixa.Add(movimentoCaixa);
                sessao.AlteradoEmUtc = agora;
                descarga.MetodoPagamentoId = metodo.Id;
                descarga.SessaoCaixaId = sessao.Id;
                descarga.Referencia = referencia;
            }
            db.DescargasCartoes.Add(descarga);
            foreach (var c in cartoes)
            {
                var saldo = c.Saldo;
                c.Saldo = 0;
                var m = Movimento(c, TipoMovimentoCartao.Descarga, -saldo, motivo, null);
                m.Descarga = descarga;
                m.MetodoPagamentoId = descarga.MetodoPagamentoId;
                m.SessaoCaixaId = descarga.SessaoCaixaId;
                m.MovimentoCaixa = movimentoCaixa;
                db.MovimentosCartao.Add(m);
            }
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
            {
                await transacao.RollbackAsync();
                if (await db.DescargasCartoes.AsNoTracking().AnyAsync(d => d.IdempotencyKey == chave))
                    throw new ConflitoException("Esta descarga já foi feita (pedido repetido). Actualize a lista.");
                throw;
            }
            auditoria.Registar("cartao.descarregar", "DescargaCartoes", descarga.Id,
                new { descarga.Total, descarga.NumeroCartoes, descarga.DevolveuDinheiro, descarga.MetodoPagamentoId, descarga.SessaoCaixaId, Motivo = motivo, Cartoes = cartoes.Select(c => c.Numero).ToList() });
            await db.SaveChangesAsync();
            await transacao.CommitAsync();
            id = descarga.Id;
        }
        return (await ObterDescargaAsync(id, default), true);
    }

    public async Task<DescargaDto> ObterDescargaAsync(int id, CancellationToken ct)
    {
        var d = await db.DescargasCartoes.AsNoTracking().Where(x => x.Id == id)
                    .Select(x => new { x.Id, x.DataUtc, x.UtilizadorId, x.DevolveuDinheiro, Metodo = x.MetodoPagamento != null ? x.MetodoPagamento.Nome : null, x.SessaoCaixaId, x.Motivo, x.Total })
                    .FirstOrDefaultAsync(ct) ?? throw new NaoEncontradoException("Descarga não encontrada.");
        var linhas = await db.MovimentosCartao.AsNoTracking().Where(m => m.DescargaId == id).OrderBy(m => m.CartaoEvento!.Numero)
            .Select(m => new { m.CartaoEvento!.Numero, m.CartaoEvento.Titular, Saldo = -m.Valor }).ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync([d.UtilizadorId], ct);
        var acumulado = 0m;
        return new DescargaDto(d.Id, d.DataUtc, nomes.GetValueOrDefault(d.UtilizadorId), d.DevolveuDinheiro, d.Metodo, d.SessaoCaixaId, d.Motivo, d.Total,
            [.. linhas.Select(l => new LinhaDescarga(l.Numero, l.Titular, l.Saldo, acumulado += l.Saldo))]);
    }
}
