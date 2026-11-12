using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Caixa;

/// <summary><see cref="Data"/>: dia de operação (por omissão, hoje no fuso configurado).</summary>
public record AbrirDia(DateOnly? Data, [StringLength(1000)] string? Observacoes);

public record FecharDia([StringLength(1000)] string? Observacoes, byte[]? RowVersion);

public record DiaOperacaoDto(int Id, DateOnly Data, EstadoDia Estado, string? Numero, string AbertoPor, DateTime AbertoEmUtc,
    string? FechadoPor, DateTime? FechadoEmUtc, int Caixas, int CaixasAbertos, byte[] RowVersion);

/// <summary>Estado para o menu Caixa: se os caixas exigem o dia aberto, o dia aberto (se houver) e o último fechado.</summary>
public record EstadoDiaDto(bool Exigir, DiaOperacaoDto? Aberto, DiaOperacaoDto? UltimoFechado, DateOnly Hoje);

/// <summary>
/// Um caixa (sessão) do dia. <see cref="Recebido"/>: pagamentos válidos (todos os métodos). Esperado, contado e diferença
/// (todos os métodos, como no fecho de caixa) só existem depois de o caixa fechar.
/// </summary>
public record CaixaDoDia(int SessaoId, string Terminal, string Operador, DateTime AbertaEmUtc, DateTime? FechadaEmUtc, bool Aberta,
    int Pedidos, decimal Recebido, decimal Fundo, decimal? Esperado, decimal? Contado, decimal? Diferenca);

/// <summary>Pagamentos de um método: um valor por caixa (pela ordem de <see cref="ResumoDia.Caixas"/>) e o total.</summary>
public record MetodoDoDia(string Metodo, IReadOnlyList<decimal> PorCaixa, decimal Total);

/// <summary>Movimentos manuais e automáticos do caixa por tipo; <see cref="Valor"/> é negativo nas saídas.</summary>
public record MovimentoDoDia(TipoMovimentoCaixa Tipo, int Quantidade, decimal Valor);

public record ProdutoDoDia(string Produto, decimal Quantidade, decimal Total);

public record CategoriaDoDia(string Categoria, decimal Quantidade, decimal Total, IReadOnlyList<ProdutoDoDia> Produtos);

/// <summary>Ocorrências a conferir. <see cref="PedidosEmAberto"/>: pedidos ainda abertos no momento do cálculo (todos os postos).</summary>
public record ControloDia(int Descontos, decimal ValorDescontos, int ItensAnulados, decimal ValorItensAnulados, int Estornos, decimal ValorEstornos,
    int PagamentosAnulados, decimal ValorPagamentosAnulados, int PedidosEmAberto);

/// <summary>
/// Resumo do dia de todos os caixas. Vendas: pedidos pagos nos caixas do dia (bruto = soma das linhas, antes de descontos;
/// líquido = total dos pedidos; IVA incluído). <see cref="TotalRecebido"/>: pagamentos que trazem dinheiro (sem Cartão Evento,
/// já recebido na recarga, nem Fatura a Crédito). <see cref="Pendencias"/>: o que impede o fecho (caixas ainda abertos).
/// </summary>
public record ResumoDia(int DiaId, string? Numero, DateOnly Data, EstadoDia Estado, string AbertoPor, DateTime AbertoEmUtc, string? FechadoPor,
    DateTime? FechadoEmUtc, string? Observacoes, int Pedidos, decimal VendasBrutas, decimal Descontos, decimal TaxasEntrega, decimal VendasLiquidas,
    decimal Impostos, decimal TicketMedio, decimal TotalRecebido, decimal TotalEntrado, decimal TotalSaido, decimal? Diferenca,
    IReadOnlyList<CaixaDoDia> Caixas, IReadOnlyList<MetodoDoDia> Metodos, IReadOnlyList<MovimentoDoDia> Movimentos,
    IReadOnlyList<CategoriaDoDia> Categorias, ControloDia Controlo, IReadOnlyList<string> Pendencias);

/// <summary>
/// Dia de operação (menu Caixa): «Abrir Dia» liberta a abertura dos caixas (se <see cref="ChavesConfiguracao.ExigirDiaAberto"/>);
/// «Fechar Dia» exige os caixas do dia todos fechados, calcula no servidor o resumo de todos os caixas e grava-o com um número
/// (série FD), para o documento se reimprimir sempre igual. Um dia fechado não se reabre; cada data só se usa uma vez.
/// </summary>
public class ServicoDia(
    IAppDbContext db,
    IUtilizadorActual actual,
    IAuditoria auditoria,
    IConsultaUtilizadores utilizadores,
    ConfiguracoesSistema config,
    ServicoNumeracao numeracao,
    TimeProvider relogio)
{
    public const string SerieFechoDia = "FD";

    public async Task<EstadoDiaDto> EstadoAsync(CancellationToken ct)
    {
        var exigir = await config.BoolAsync(ChavesConfiguracao.ExigirDiaAberto, padrao: true);
        var aberto = await db.DiasOperacao.AsNoTracking().Where(d => d.Estado == EstadoDia.Aberto).Select(d => d.Id).FirstOrDefaultAsync(ct);
        var fechado = await db.DiasOperacao.AsNoTracking().Where(d => d.Estado == EstadoDia.Fechado).OrderByDescending(d => d.Data)
            .Select(d => d.Id).FirstOrDefaultAsync(ct);
        var dias = await DtosAsync([aberto, fechado], ct);
        return new EstadoDiaDto(exigir, dias.GetValueOrDefault(aberto), dias.GetValueOrDefault(fechado), await HojeAsync());
    }

    public async Task<Pagina<DiaOperacaoDto>> ListarAsync(int pagina, int tamanho, CancellationToken ct)
    {
        (pagina, tamanho) = Pagina<DiaOperacaoDto>.Normalizar(pagina, tamanho);
        var total = await db.DiasOperacao.CountAsync(ct);
        var ids = await db.DiasOperacao.AsNoTracking().OrderByDescending(d => d.Data).Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(d => d.Id).ToListAsync(ct);
        var dias = await DtosAsync(ids, ct);
        return new Pagina<DiaOperacaoDto>([.. ids.Select(id => dias[id])], total, pagina, tamanho);
    }

    public async Task<DiaOperacaoDto> AbrirAsync(AbrirDia dados)
    {
        var utilizadorId = actual.Id ?? throw new RegraNegocioException("Sessão de utilizador inválida.");
        var hoje = await HojeAsync();
        var data = dados.Data ?? hoje;
        if (data > hoje)
            throw new RegraNegocioException("Não pode abrir um dia futuro.");
        if (await db.DiasOperacao.Where(d => d.Estado == EstadoDia.Aberto).Select(d => (DateOnly?)d.Data).FirstOrDefaultAsync() is DateOnly aberto)
            throw new ConflitoException($"O dia {aberto:dd/MM/yyyy} ainda está aberto. Feche-o antes de abrir outro.");
        if (await db.DiasOperacao.MaxAsync(d => (DateOnly?)d.Data) is DateOnly ultimo && data <= ultimo)
            throw new ConflitoException(data == ultimo
                ? $"O dia {data:dd/MM/yyyy} já foi aberto e fechado. Um dia fechado não se reabre."
                : $"O último dia de operação foi {ultimo:dd/MM/yyyy}: só pode abrir uma data posterior.");

        await using var transacao = await db.Database.BeginTransactionAsync();
        var dia = new DiaOperacao
        {
            Data = data,
            UtilizadorAberturaId = utilizadorId,
            AbertoEmUtc = relogio.GetUtcNow().UtcDateTime,
            ObservacoesAbertura = string.IsNullOrWhiteSpace(dados.Observacoes) ? null : dados.Observacoes.Trim(),
        };
        db.DiasOperacao.Add(dia);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Dois «Abrir Dia» ao mesmo tempo: os índices únicos (data e dia aberto) rejeitam o segundo.
            throw new ConflitoException("Já há um dia aberto.");
        }
        auditoria.Registar("caixa.dia-abrir", "DiaOperacao", dia.Id, new { Data = data.ToString("yyyy-MM-dd") });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return (await DtosAsync([dia.Id], default))[dia.Id];
    }

    /// <summary>Resumo do dia: o gravado no fecho, ou calculado agora (ponto de situação) se o dia está aberto.</summary>
    public async Task<ResumoDia> ResumoAsync(int id, CancellationToken ct)
    {
        var dia = await db.DiasOperacao.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NaoEncontradoException("Dia não encontrado.");
        if (dia.Estado == EstadoDia.Fechado && dia.Resumo is not null)
            return JsonSerializer.Deserialize<ResumoDia>(dia.Resumo)!;
        return await CalcularAsync(dia, ct);
    }

    public async Task<ResumoDia> FecharAsync(int id, FecharDia dados)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var dia = await db.DiasOperacao.FirstOrDefaultAsync(d => d.Id == id) ?? throw new NaoEncontradoException("Dia não encontrado.");
        if (dia.Estado != EstadoDia.Aberto)
            throw new RegraNegocioException("Este dia já está fechado.");
        db.VerificarVersao(dia, dados.RowVersion);

        var resumo = await CalcularAsync(dia, default);
        if (resumo.Pendencias.Count > 0)
            throw new RegraNegocioException("Feche primeiro todos os caixas do dia: " + string.Join("; ", resumo.Pendencias) + ".");

        dia.Estado = EstadoDia.Fechado;
        dia.FechadoEmUtc = relogio.GetUtcNow().UtcDateTime;
        dia.UtilizadorFechoId = actual.Id;
        dia.ObservacoesFecho = string.IsNullOrWhiteSpace(dados.Observacoes) ? null : dados.Observacoes.Trim();
        dia.Numero = await numeracao.ProximoAsync(SerieFechoDia);
        var nomes = await utilizadores.NomesAsync(actual.Id is int u ? [u] : []);
        resumo = resumo with
        {
            Numero = dia.Numero, Estado = EstadoDia.Fechado, FechadoEmUtc = dia.FechadoEmUtc,
            FechadoPor = actual.Id is int f ? nomes.GetValueOrDefault(f) : null, Observacoes = dia.ObservacoesFecho,
        };
        dia.Resumo = JsonSerializer.Serialize(resumo);
        auditoria.Registar("caixa.dia-fechar", "DiaOperacao", dia.Id, new
        {
            Data = dia.Data.ToString("yyyy-MM-dd"), dia.Numero, Caixas = resumo.Caixas.Count, resumo.Pedidos, resumo.VendasLiquidas, resumo.Diferenca,
        });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return resumo;
    }

    // ---------- Cálculo ----------

    private async Task<ResumoDia> CalcularAsync(DiaOperacao dia, CancellationToken ct)
    {
        var sessoes = await db.SessoesCaixa.AsNoTracking().Where(s => s.DiaOperacaoId == dia.Id).OrderBy(s => s.AbertaEmUtc).ThenBy(s => s.Id)
            .Select(s => new { s.Id, Terminal = s.Terminal.Nome, s.UtilizadorAberturaId, s.AbertaEmUtc, s.FechadaEmUtc, s.Estado, s.FundoInicial })
            .ToListAsync(ct);
        var ids = sessoes.Select(s => s.Id).ToList();

        var pagamentos = await db.Pagamentos.AsNoTracking().Where(p => ids.Contains(p.SessaoCaixaId))
            .Select(p => new { p.SessaoCaixaId, p.PedidoId, p.MetodoPagamentoId, Metodo = p.MetodoPagamento.Nome, p.MetodoPagamento.Codigo, p.MetodoPagamento.Ordem, p.Valor, p.Estado })
            .ToListAsync(ct);
        var validos = pagamentos.Where(p => p.Estado == EstadoPagamento.Valido).ToList();
        var fecho = await db.FechoCaixaLinhas.AsNoTracking().Where(l => ids.Contains(l.SessaoCaixaId))
            .GroupBy(l => l.SessaoCaixaId)
            .Select(g => new { SessaoId = g.Key, Esperado = g.Sum(l => l.ValorEsperado), Contado = g.Sum(l => l.ValorContado), Diferenca = g.Sum(l => l.Diferenca) })
            .ToDictionaryAsync(x => x.SessaoId, ct);
        var movimentos = await db.MovimentosCaixa.AsNoTracking().Where(m => ids.Contains(m.SessaoCaixaId))
            .GroupBy(m => m.Tipo).Select(g => new { Tipo = g.Key, Quantidade = g.Count(), Valor = g.Sum(m => m.Valor) })
            .ToListAsync(ct);
        var pedidos = await db.Pedidos.AsNoTracking()
            .Where(p => p.SessaoCaixaId != null && ids.Contains(p.SessaoCaixaId.Value) && (p.Estado == EstadoPedido.Pago || p.Estado == EstadoPedido.Estornado))
            .Select(p => new { p.Estado, p.Subtotal, p.Desconto, p.TaxaEntrega, p.Impostos, p.Total })
            .ToListAsync(ct);
        var pagos = pedidos.Where(p => p.Estado == EstadoPedido.Pago).ToList();
        var produtos = await db.PedidoItens.AsNoTracking()
            .Where(i => !i.Cancelado && i.Pedido.SessaoCaixaId != null && ids.Contains(i.Pedido.SessaoCaixaId.Value) && i.Pedido.Estado == EstadoPedido.Pago)
            .GroupBy(i => new { Categoria = i.Produto.Categoria.Nome, i.NomeProduto })
            .Select(g => new { g.Key.Categoria, g.Key.NomeProduto, Quantidade = g.Sum(i => i.Quantidade), Total = g.Sum(i => i.Total) })
            .ToListAsync(ct);
        var anulados = await db.PedidoItens.AsNoTracking()
            .Where(i => i.Cancelado && i.Pedido.SessaoCaixaId != null && ids.Contains(i.Pedido.SessaoCaixaId.Value))
            .GroupBy(_ => 1).Select(g => new { Quantidade = g.Count(), Total = g.Sum(i => i.Total) })
            .FirstOrDefaultAsync(ct);
        var emAberto = await db.Pedidos.CountAsync(p => p.Estado == EstadoPedido.Aberto, ct);

        var nomes = await utilizadores.NomesAsync(sessoes.Select(s => s.UtilizadorAberturaId).Append(dia.UtilizadorAberturaId)
            .Concat(dia.UtilizadorFechoId is int uf ? [uf] : []).Distinct(), ct);

        var caixas = sessoes.Select(s =>
        {
            var f = fecho.GetValueOrDefault(s.Id);
            var aberta = s.Estado == EstadoSessaoCaixa.Aberta;
            var daSessao = validos.Where(p => p.SessaoCaixaId == s.Id).ToList();
            return new CaixaDoDia(s.Id, s.Terminal, nomes.GetValueOrDefault(s.UtilizadorAberturaId, "?"), s.AbertaEmUtc, s.FechadaEmUtc, aberta,
                daSessao.Select(p => p.PedidoId).Distinct().Count(), daSessao.Sum(p => p.Valor), s.FundoInicial,
                aberta ? null : f?.Esperado ?? 0m, aberta ? null : f?.Contado ?? 0m, aberta ? null : f?.Diferenca ?? 0m);
        }).ToList();

        var metodos = validos.GroupBy(p => new { p.MetodoPagamentoId, p.Metodo, p.Ordem }).OrderBy(g => g.Key.Ordem)
            .Select(g => new MetodoDoDia(g.Key.Metodo, [.. sessoes.Select(s => g.Where(p => p.SessaoCaixaId == s.Id).Sum(p => p.Valor))], g.Sum(p => p.Valor)))
            .ToList();

        static bool Saida(TipoMovimentoCaixa t) => t != TipoMovimentoCaixa.Entrada;
        var porTipo = new[] { TipoMovimentoCaixa.Entrada, TipoMovimentoCaixa.Saida, TipoMovimentoCaixa.Despesa, TipoMovimentoCaixa.Reembolso }
            .Select(t => movimentos.FirstOrDefault(m => m.Tipo == t) is { } m ? new MovimentoDoDia(t, m.Quantidade, Saida(t) ? -m.Valor : m.Valor) : null)
            .OfType<MovimentoDoDia>().ToList();

        var categorias = produtos.GroupBy(p => p.Categoria).OrderByDescending(g => g.Sum(p => p.Total))
            .Select(g => new CategoriaDoDia(g.Key, g.Sum(p => p.Quantidade), g.Sum(p => p.Total),
                [.. g.OrderByDescending(p => p.Total).ThenBy(p => p.NomeProduto).Select(p => new ProdutoDoDia(p.NomeProduto, p.Quantidade, p.Total))]))
            .ToList();

        var semDinheiro = new[] { Cartoes.ServicoCartoes.CodigoMetodo, Facturacao.ServicoDocumentos.CodigoFacturaCredito };
        var recebido = validos.Where(p => !semDinheiro.Contains(p.Codigo)).Sum(p => p.Valor);
        var estornos = pedidos.Where(p => p.Estado == EstadoPedido.Estornado).ToList();
        var anuladosPag = pagamentos.Where(p => p.Estado == EstadoPagamento.Anulado).ToList();
        var liquidas = pagos.Sum(p => p.Total);

        return new ResumoDia(
            dia.Id, dia.Numero, dia.Data, dia.Estado, nomes.GetValueOrDefault(dia.UtilizadorAberturaId, "?"), dia.AbertoEmUtc,
            dia.UtilizadorFechoId is int fp ? nomes.GetValueOrDefault(fp) : null, dia.FechadoEmUtc, dia.ObservacoesFecho,
            pagos.Count, pagos.Sum(p => p.Subtotal), pagos.Sum(p => p.Desconto), pagos.Sum(p => p.TaxaEntrega), liquidas, pagos.Sum(p => p.Impostos),
            pagos.Count == 0 ? 0m : decimal.Round(liquidas / pagos.Count, 2, MidpointRounding.AwayFromZero),
            recebido,
            recebido + porTipo.Where(m => m.Valor > 0).Sum(m => m.Valor),
            porTipo.Where(m => m.Valor < 0).Sum(m => m.Valor),
            caixas.Count == 0 || caixas.Any(c => c.Aberta) ? null : caixas.Sum(c => c.Diferenca ?? 0m),
            caixas, metodos, porTipo, categorias,
            new ControloDia(pagos.Count(p => p.Desconto > 0), pagos.Sum(p => p.Desconto), anulados?.Quantidade ?? 0, anulados?.Total ?? 0m,
                estornos.Count, estornos.Sum(p => p.Total), anuladosPag.Count, anuladosPag.Sum(p => p.Valor), emAberto),
            [.. caixas.Where(c => c.Aberta).Select(c => $"caixa {c.Terminal} de {c.Operador} (sessão n.º {c.SessaoId}) ainda aberto")]);
    }

    private async Task<DateOnly> HojeAsync() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(relogio.GetUtcNow().UtcDateTime, await config.FusoAsync()));

    private async Task<Dictionary<int, DiaOperacaoDto>> DtosAsync(IReadOnlyList<int> ids, CancellationToken ct)
    {
        var lista = ids.Where(i => i != 0).Distinct().ToList();
        var dias = await db.DiasOperacao.AsNoTracking().Where(d => lista.Contains(d.Id))
            .Select(d => new
            {
                d.Id, d.Data, d.Estado, d.Numero, d.UtilizadorAberturaId, d.AbertoEmUtc, d.UtilizadorFechoId, d.FechadoEmUtc, d.RowVersion,
                Caixas = d.Sessoes.Count, Abertos = d.Sessoes.Count(s => s.Estado == EstadoSessaoCaixa.Aberta),
            })
            .ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(dias.Select(d => d.UtilizadorAberturaId).Concat(dias.Where(d => d.UtilizadorFechoId != null).Select(d => d.UtilizadorFechoId!.Value)).Distinct(), ct);
        return dias.ToDictionary(d => d.Id, d => new DiaOperacaoDto(d.Id, d.Data, d.Estado, d.Numero, nomes.GetValueOrDefault(d.UtilizadorAberturaId, "?"),
            d.AbertoEmUtc, d.UtilizadorFechoId is int f ? nomes.GetValueOrDefault(f) : null, d.FechadoEmUtc, d.Caixas, d.Abertos, d.RowVersion));
    }
}
