using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Compras;
using LAMGestRestaurant.Domain.Facturacao;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Relatorios;

// ---------- Contas a pagar (fornecedores) ----------
public record LinhaCompraAberta(int Id, string Fornecedor, string Documento, DateOnly Data, DateOnly? Vencimento, decimal Total, decimal Pago, decimal EmDivida, int DiasAtraso);
public record DividaFornecedor(int FornecedorId, string Fornecedor, int Compras, decimal EmDivida, decimal NaoVencido, decimal Ate30, decimal De31a60, decimal De61a90, decimal Mais90);
public record LinhaPagamentoFornecedor(DateTime DataUtc, string Fornecedor, string Documento, string Metodo, string? Referencia, decimal Valor, bool Anulado);
public record RelatorioContasPagar(DateOnly Desde, DateOnly Ate, DateOnly Hoje, decimal TotalEmDivida, decimal TotalVencido, IReadOnlyList<DividaFornecedor> Dividas,
    IReadOnlyList<LinhaCompraAberta> ComprasEmAberto, decimal PagoNoPeriodo, IReadOnlyList<LinhaPagamentoFornecedor> Pagamentos, IReadOnlyList<LinhaValorQuantidade> PorMetodo);

/// <summary>Linha genérica «nome · quantidade · valor».</summary>
public record LinhaValorQuantidade(string Nome, int Quantidade, decimal Valor);

// ---------- Vendas por hora e dia da semana ----------
/// <summary><see cref="DiaSemana"/>: 0 = segunda … 6 = domingo.</summary>
public record CelulaHora(int DiaSemana, int Hora, int Pedidos, decimal Valor);
public record LinhaHora(int Hora, int Pedidos, decimal Valor, decimal ValorMedio);
/// <summary><see cref="Dias"/>: quantos dias desse dia da semana houve no período (para a média por dia).</summary>
public record LinhaDiaSemana(int DiaSemana, string Nome, int Pedidos, decimal Valor, int Dias, decimal MediaPorDia);
public record RelatorioHoras(DateOnly Desde, DateOnly Ate, int Pedidos, decimal Valor, IReadOnlyList<LinhaHora> PorHora, IReadOnlyList<LinhaDiaSemana> PorDiaSemana,
    IReadOnlyList<CelulaHora> Celulas, int? HoraPico, string? DiaPico);

// ---------- Cotações ----------
public enum SituacaoCotacao { Aberta, Expirada, Convertida, Anulada }
public record LinhaCotacao(int Id, string Numero, DateTime EmitidoEmUtc, DateOnly? ValidaAte, string Cliente, decimal Total, SituacaoCotacao Situacao, string? Factura, int? DiasAteConversao);
public record RelatorioCotacoes(DateOnly Desde, DateOnly Ate, int Emitidas, decimal ValorEmitido, int Convertidas, decimal ValorConvertido, int Abertas, decimal ValorAberto,
    int Expiradas, decimal ValorExpirado, int Anuladas, decimal TaxaConversao, decimal? DiasMedioConversao, IReadOnlyList<LinhaCotacao> Linhas, bool Cortado);

// ---------- Tempos da cozinha ----------
public record LinhaTempo(string Nome, string? Estacao, int Itens, decimal MediaMinutos, decimal MaximoMinutos, int Atrasados);
public record ItemLento(DateTime EnviadoEmUtc, string Pedido, string? Mesa, string Produto, string? Estacao, decimal Minutos);
public record RelatorioCozinha(DateOnly Desde, DateOnly Ate, int LimiteMinutos, int Itens, decimal MediaMinutos, decimal MedianaMinutos, int Atrasados,
    IReadOnlyList<LinhaTempo> PorEstacao, IReadOnlyList<LinhaTempo> PorPrato, IReadOnlyList<ItemLento> MaisLentos, int PorTerminar);

// ---------- Delivery e Take Away ----------
public record LinhaEstafeta(string Estafeta, int Entregas, decimal Valor, decimal TaxaEntrega, decimal? MediaMinutos);
public record LinhaTipoEntrega(TipoPedido Tipo, int Pedidos, int Pagos, decimal Valor, int Cancelados, decimal? MediaMinutos);
public record RelatorioDelivery(DateOnly Desde, DateOnly Ate, int Pedidos, decimal Valor, decimal TaxaEntrega, int Cancelados, decimal? MediaMinutosEntrega,
    IReadOnlyList<LinhaTipoEntrega> PorTipo, IReadOnlyList<LinhaEstafeta> PorEstafeta);

// ---------- Mesas ----------
public record LinhaMesaRelatorio(string Mesa, string Zona, int Pedidos, int Pessoas, decimal Valor, decimal MediaMinutos, decimal? ValorPorPessoa);
public record RelatorioMesas(DateOnly Desde, DateOnly Ate, int Pedidos, int Pessoas, decimal Valor, decimal MediaMinutos, decimal? ValorPorPessoa,
    IReadOnlyList<LinhaMesaRelatorio> PorZona, IReadOnlyList<LinhaMesaRelatorio> PorMesa);

// ---------- Pontos de fidelização ----------
public record LinhaPontosCliente(int ClienteId, string Cliente, string? Telefone, int Ganhos, int Usados, int Ajustes, int Estornos, int Saldo);
public record RelatorioPontos(DateOnly Desde, DateOnly Ate, int Ganhos, int Usados, int Ajustes, int Estornos, int SaldoTotal, int ClientesComSaldo,
    IReadOnlyList<LinhaPontosCliente> Clientes);

// ---------- Reservas ----------
// PorDiaSemana: Quantidade = reservas, Valor = pessoas.
public enum SituacaoReserva { Compareceu, Faltou, Cancelada, PorVir }
public record LinhaReservaRelatorio(DateTime InicioUtc, string Cliente, string? Telefone, int Pessoas, string? Mesa, SituacaoReserva Situacao, string? Motivo);
public record RelatorioReservas(DateOnly Desde, DateOnly Ate, int Reservas, int Pessoas, int Compareceram, int Faltaram, int Canceladas, int PorVir,
    decimal? TaxaComparencia, IReadOnlyList<LinhaValorQuantidade> PorDiaSemana, IReadOnlyList<LinhaReservaRelatorio> Linhas, bool Cortado);

/// <summary>
/// Relatórios de operação: contas a pagar aos fornecedores, vendas por hora, cotações, tempos da cozinha, delivery, mesas,
/// pontos de fidelização e reservas. Datas no fuso configurado; vendas = pedidos pagos (como no relatório de vendas).
/// </summary>
public class ServicoRelatoriosOperacao(IAppDbContext db, ConfiguracoesSistema config, TimeProvider relogio)
{
    public const int MaximoLinhas = 5000;
    public const int LimiteCozinhaPadrao = 20;
    private static readonly string[] NomesDias = ["Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado", "Domingo"];

    public static string NomeDia(int diaSemana) => NomesDias[diaSemana];
    private static int DiaSemana(DateTime local) => ((int)local.DayOfWeek + 6) % 7;
    private static decimal Min(TimeSpan t) => Math.Round((decimal)t.TotalMinutes, 1);
    private static decimal Media(IEnumerable<decimal> v) => v.Any() ? Math.Round(v.Average(), 1) : 0;

    public async Task<RelatorioContasPagar> ContasPagarAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        var hoje = await config.HojeAsync(relogio);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var compras = await db.Compras.AsNoTracking().Where(c => c.Estado == EstadoCompra.Confirmada)
            .Select(c => new
            {
                c.Id, c.FornecedorId, Fornecedor = c.Fornecedor.Nome, c.NumeroDocumento, c.DataDocumento, c.DataVencimento, c.Total,
                Pago = db.PagamentosCompras.Where(p => p.CompraId == c.Id && p.Estado == EstadoPagamentoCompra.Valido).Sum(p => (decimal?)p.Valor) ?? 0m,
            })
            .ToListAsync(ct);
        var abertas = compras.Where(c => c.Total - c.Pago > 0)
            .Select(c => new LinhaCompraAberta(c.Id, c.Fornecedor, c.NumeroDocumento, c.DataDocumento, c.DataVencimento, c.Total, c.Pago, c.Total - c.Pago,
                c.DataVencimento is DateOnly v && v < hoje ? hoje.DayNumber - v.DayNumber : 0))
            .OrderByDescending(c => c.DiasAtraso).ThenBy(c => c.Vencimento ?? c.Data).ToList();
        var dividas = abertas.GroupBy(c => compras.First(x => x.Id == c.Id).FornecedorId).Select(g => new DividaFornecedor(g.Key, g.First().Fornecedor, g.Count(), g.Sum(c => c.EmDivida),
                g.Where(c => c.DiasAtraso == 0).Sum(c => c.EmDivida), g.Where(c => c.DiasAtraso is > 0 and <= 30).Sum(c => c.EmDivida),
                g.Where(c => c.DiasAtraso is > 30 and <= 60).Sum(c => c.EmDivida), g.Where(c => c.DiasAtraso is > 60 and <= 90).Sum(c => c.EmDivida),
                g.Where(c => c.DiasAtraso > 90).Sum(c => c.EmDivida)))
            .OrderByDescending(d => d.EmDivida).ToList();

        var pagamentos = await db.PagamentosCompras.AsNoTracking().Where(p => p.DataUtc >= inicio && p.DataUtc < fim).OrderBy(p => p.DataUtc).Take(MaximoLinhas)
            .Select(p => new LinhaPagamentoFornecedor(p.DataUtc, p.Compra.Fornecedor.Nome, p.Compra.NumeroDocumento, p.MetodoPagamento.Nome, p.Referencia, p.Valor,
                p.Estado == EstadoPagamentoCompra.Anulado))
            .ToListAsync(ct);
        var validos = pagamentos.Where(p => !p.Anulado).ToList();
        return new RelatorioContasPagar(desde, ate, hoje, abertas.Sum(c => c.EmDivida), abertas.Where(c => c.DiasAtraso > 0).Sum(c => c.EmDivida), dividas,
            [.. abertas.Take(MaximoLinhas)], validos.Sum(p => p.Valor), pagamentos,
            [.. validos.GroupBy(p => p.Metodo).Select(g => new LinhaValorQuantidade(g.Key, g.Count(), g.Sum(p => p.Valor))).OrderByDescending(x => x.Valor)]);
    }

    public async Task<RelatorioHoras> VendasPorHoraAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var fuso = await config.FusoAsync();
        var vendas = await db.Pedidos.AsNoTracking().Where(p => p.Estado == EstadoPedido.Pago && p.FechadoEmUtc >= inicio && p.FechadoEmUtc < fim)
            .Select(p => new { Fechado = p.FechadoEmUtc!.Value, p.Total }).ToListAsync(ct);
        var locais = vendas.Select(v => (Local: TimeZoneInfo.ConvertTimeFromUtc(v.Fechado, fuso), v.Total)).ToList();
        var celulas = locais.GroupBy(v => (DiaSemana(v.Local), v.Local.Hour))
            .Select(g => new CelulaHora(g.Key.Item1, g.Key.Hour, g.Count(), g.Sum(v => v.Total))).OrderBy(c => c.DiaSemana).ThenBy(c => c.Hora).ToList();
        var porHora = locais.GroupBy(v => v.Local.Hour).OrderBy(g => g.Key)
            .Select(g => new LinhaHora(g.Key, g.Count(), g.Sum(v => v.Total), Math.Round(g.Average(v => v.Total), 2))).ToList();
        var dias = Enumerable.Range(0, ate.DayNumber - desde.DayNumber + 1).Select(i => desde.AddDays(i)).GroupBy(d => ((int)d.DayOfWeek + 6) % 7).ToDictionary(g => g.Key, g => g.Count());
        var porDia = Enumerable.Range(0, 7).Select(d =>
        {
            var doDia = locais.Where(v => DiaSemana(v.Local) == d).ToList();
            var n = dias.GetValueOrDefault(d);
            return new LinhaDiaSemana(d, NomesDias[d], doDia.Count, doDia.Sum(v => v.Total), n, n == 0 ? 0 : Math.Round(doDia.Sum(v => v.Total) / n, 2));
        }).ToList();
        return new RelatorioHoras(desde, ate, vendas.Count, vendas.Sum(v => v.Total), porHora, porDia, celulas,
            porHora.Count == 0 ? null : porHora.MaxBy(h => h.Valor)!.Hora, vendas.Count == 0 ? null : porDia.MaxBy(d => d.MediaPorDia)!.Nome);
    }

    public async Task<RelatorioCotacoes> CotacoesAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        var hoje = await config.HojeAsync(relogio);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var linhas = await db.Documentos.AsNoTracking().Where(d => d.Tipo == TipoDocumento.Cotacao && d.EmitidoEmUtc >= inicio && d.EmitidoEmUtc < fim)
            .OrderBy(d => d.EmitidoEmUtc).Take(MaximoLinhas + 1)
            .Select(d => new
            {
                d.Id, d.Numero, d.EmitidoEmUtc, d.ValidaAte, Cliente = d.ClienteNome, d.Total, d.Estado,
                Factura = db.Documentos.Where(f => f.DocumentoOrigemId == d.Id && f.Tipo == TipoDocumento.Factura).Select(f => new { f.Numero, f.EmitidoEmUtc }).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var cotacoes = linhas.Take(MaximoLinhas).Select(d =>
        {
            var situacao = d.Estado switch
            {
                EstadoDocumento.Convertido => SituacaoCotacao.Convertida,
                EstadoDocumento.Anulado => SituacaoCotacao.Anulada,
                _ => d.ValidaAte is DateOnly v && v < hoje ? SituacaoCotacao.Expirada : SituacaoCotacao.Aberta,
            };
            int? diasConv = d.Factura is { } f ? Math.Max(0, (int)(f.EmitidoEmUtc - d.EmitidoEmUtc).TotalDays) : null;
            return new LinhaCotacao(d.Id, d.Numero, d.EmitidoEmUtc, d.ValidaAte, d.Cliente ?? "—", d.Total, situacao, d.Factura?.Numero, diasConv);
        }).ToList();
        decimal Valor(SituacaoCotacao s) => cotacoes.Where(c => c.Situacao == s).Sum(c => c.Total);
        int Conta(SituacaoCotacao s) => cotacoes.Count(c => c.Situacao == s);
        var validas = cotacoes.Count - Conta(SituacaoCotacao.Anulada);
        var dias = cotacoes.Where(c => c.DiasAteConversao is not null).Select(c => (decimal)c.DiasAteConversao!.Value).ToList();
        return new RelatorioCotacoes(desde, ate, cotacoes.Count, cotacoes.Sum(c => c.Total), Conta(SituacaoCotacao.Convertida), Valor(SituacaoCotacao.Convertida),
            Conta(SituacaoCotacao.Aberta), Valor(SituacaoCotacao.Aberta), Conta(SituacaoCotacao.Expirada), Valor(SituacaoCotacao.Expirada), Conta(SituacaoCotacao.Anulada),
            validas == 0 ? 0 : Math.Round(100m * Conta(SituacaoCotacao.Convertida) / validas, 1), dias.Count == 0 ? null : Math.Round(dias.Average(), 1),
            cotacoes, linhas.Count > MaximoLinhas);
    }

    public async Task<RelatorioCozinha> CozinhaAsync(DateOnly desde, DateOnly ate, int limiteMinutos = LimiteCozinhaPadrao, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        limiteMinutos = Math.Clamp(limiteMinutos, 1, 240);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var enviados = db.PedidoItens.AsNoTracking().Where(i => !i.Cancelado && i.EnviadoCozinhaEmUtc >= inicio && i.EnviadoCozinhaEmUtc < fim);
        var porTerminar = await enviados.CountAsync(i => i.ProntoEmUtc == null, ct);
        var itens = await enviados.Where(i => i.ProntoEmUtc != null)
            .Select(i => new
            {
                Enviado = i.EnviadoCozinhaEmUtc!.Value, Pronto = i.ProntoEmUtc!.Value, i.NomeProduto, Pedido = i.Pedido.Numero,
                Mesa = i.Pedido.Mesa != null ? i.Pedido.Mesa.Nome : null,
                Estacao = i.Produto.Categoria.EstacaoPreparacao != null ? i.Produto.Categoria.EstacaoPreparacao.Nome
                    : i.Produto.Categoria.CategoriaPai != null && i.Produto.Categoria.CategoriaPai.EstacaoPreparacao != null ? i.Produto.Categoria.CategoriaPai.EstacaoPreparacao.Nome : null,
            })
            .ToListAsync(ct);
        var tempos = itens.Select(i => (i, Minutos: Min(i.Pronto - i.Enviado))).Where(x => x.Minutos >= 0).ToList();
        var porEstacao = tempos.GroupBy(x => x.i.Estacao ?? "Sem estação")
            .Select(g => new LinhaTempo(g.Key, null, g.Count(), Media(g.Select(x => x.Minutos)), g.Max(x => x.Minutos), g.Count(x => x.Minutos > limiteMinutos)))
            .OrderByDescending(l => l.MediaMinutos).ToList();
        var porPrato = tempos.GroupBy(x => (x.i.NomeProduto, x.i.Estacao))
            .Select(g => new LinhaTempo(g.Key.NomeProduto, g.Key.Estacao, g.Count(), Media(g.Select(x => x.Minutos)), g.Max(x => x.Minutos), g.Count(x => x.Minutos > limiteMinutos)))
            .OrderByDescending(l => l.MediaMinutos).Take(MaximoLinhas).ToList();
        var lentos = tempos.OrderByDescending(x => x.Minutos).Take(20)
            .Select(x => new ItemLento(x.i.Enviado, x.i.Pedido, x.i.Mesa, x.i.NomeProduto, x.i.Estacao, x.Minutos)).ToList();
        var ordenados = tempos.Select(x => x.Minutos).Order().ToList();
        var mediana = ordenados.Count == 0 ? 0 : ordenados.Count % 2 == 1 ? ordenados[ordenados.Count / 2] : Math.Round((ordenados[ordenados.Count / 2 - 1] + ordenados[ordenados.Count / 2]) / 2, 1);
        return new RelatorioCozinha(desde, ate, limiteMinutos, tempos.Count, Media(ordenados), mediana, tempos.Count(x => x.Minutos > limiteMinutos),
            porEstacao, porPrato, lentos, porTerminar);
    }

    public async Task<RelatorioDelivery> DeliveryAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var pedidos = await db.Pedidos.AsNoTracking()
            .Where(p => (p.Tipo == TipoPedido.Delivery || p.Tipo == TipoPedido.TakeAway) && p.CriadoEmUtc >= inicio && p.CriadoEmUtc < fim)
            .Select(p => new
            {
                p.Tipo, p.Estado, p.Total, p.TaxaEntrega, p.CriadoEmUtc, Estafeta = p.Estafeta != null ? p.Estafeta.Nome : null,
                Entregue = db.HistoricoPedidos.Where(h => h.PedidoId == p.Id && h.Accao == "entrega" && h.EstadoNovo == "Entregue").Min(h => (DateTime?)h.DataUtc),
            })
            .ToListAsync(ct);
        static decimal? MediaOuNulo(IEnumerable<decimal> v) => v.Any() ? Math.Round(v.Average(), 1) : null;
        var pagos = pedidos.Where(p => p.Estado == EstadoPedido.Pago).ToList();
        var porTipo = pedidos.GroupBy(p => p.Tipo).Select(g => new LinhaTipoEntrega(g.Key, g.Count(), g.Count(p => p.Estado == EstadoPedido.Pago),
                g.Where(p => p.Estado == EstadoPedido.Pago).Sum(p => p.Total), g.Count(p => p.Estado == EstadoPedido.Cancelado),
                MediaOuNulo(g.Where(p => p.Entregue is not null).Select(p => Min(p.Entregue!.Value - p.CriadoEmUtc)))))
            .OrderBy(l => l.Tipo).ToList();
        var porEstafeta = pedidos.Where(p => p.Tipo == TipoPedido.Delivery && p.Estado != EstadoPedido.Cancelado).GroupBy(p => p.Estafeta ?? "Sem estafeta")
            .Select(g => new LinhaEstafeta(g.Key, g.Count(), g.Where(p => p.Estado == EstadoPedido.Pago).Sum(p => p.Total), g.Where(p => p.Estado == EstadoPedido.Pago).Sum(p => p.TaxaEntrega),
                MediaOuNulo(g.Where(p => p.Entregue is not null).Select(p => Min(p.Entregue!.Value - p.CriadoEmUtc)))))
            .OrderByDescending(l => l.Entregas).ToList();
        return new RelatorioDelivery(desde, ate, pedidos.Count, pagos.Sum(p => p.Total), pagos.Sum(p => p.TaxaEntrega), pedidos.Count(p => p.Estado == EstadoPedido.Cancelado),
            MediaOuNulo(pedidos.Where(p => p.Tipo == TipoPedido.Delivery && p.Entregue is not null).Select(p => Min(p.Entregue!.Value - p.CriadoEmUtc))), porTipo, porEstafeta);
    }

    public async Task<RelatorioMesas> MesasAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var pedidos = await db.Pedidos.AsNoTracking()
            .Where(p => p.Tipo == TipoPedido.Mesa && p.MesaId != null && p.Estado == EstadoPedido.Pago && p.FechadoEmUtc >= inicio && p.FechadoEmUtc < fim)
            .Select(p => new { Mesa = p.Mesa!.Nome, Zona = p.Mesa.Zona.Nome, p.NumeroPessoas, p.Total, Minutos = (p.FechadoEmUtc!.Value - p.CriadoEmUtc) })
            .ToListAsync(ct);
        LinhaMesaRelatorio Linha(string mesa, string zona, IReadOnlyCollection<(int Pessoas, decimal Total, decimal Minutos)> g)
        {
            var pessoas = g.Sum(x => x.Pessoas);
            return new LinhaMesaRelatorio(mesa, zona, g.Count, pessoas, g.Sum(x => x.Total), Media(g.Select(x => x.Minutos)), pessoas == 0 ? null : Math.Round(g.Sum(x => x.Total) / pessoas, 2));
        }
        var lista = pedidos.Select(p => (p.Mesa, p.Zona, Dados: (p.NumeroPessoas, p.Total, Min(p.Minutos)))).ToList();
        var porMesa = lista.GroupBy(x => (x.Zona, x.Mesa)).Select(g => Linha(g.Key.Mesa, g.Key.Zona, [.. g.Select(x => x.Dados)])).OrderByDescending(l => l.Valor).ToList();
        var porZona = lista.GroupBy(x => x.Zona).Select(g => Linha(g.Key, g.Key, [.. g.Select(x => x.Dados)])).OrderByDescending(l => l.Valor).ToList();
        var total = Linha("", "", [.. lista.Select(x => x.Dados)]);
        return new RelatorioMesas(desde, ate, total.Pedidos, total.Pessoas, total.Valor, total.MediaMinutos, total.ValorPorPessoa, porZona, porMesa);
    }

    public async Task<RelatorioPontos> PontosAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var movimentos = await db.MovimentosPontos.AsNoTracking().Where(m => m.DataUtc >= inicio && m.DataUtc < fim)
            .GroupBy(m => new { m.ClienteId, m.Tipo }).Select(g => new { g.Key.ClienteId, g.Key.Tipo, Pontos = g.Sum(m => m.Pontos) }).ToListAsync(ct);
        var ids = movimentos.Select(m => m.ClienteId).Distinct().ToList();
        var clientes = await db.Clientes.AsNoTracking().Where(c => ids.Contains(c.Id) || c.SaldoPontos != 0)
            .Select(c => new { c.Id, c.Nome, c.Telefone, c.SaldoPontos }).ToListAsync(ct);
        int Soma(int? cliente, TipoMovimentoPontos tipo) => movimentos.Where(m => (cliente is null || m.ClienteId == cliente) && m.Tipo == tipo).Sum(m => m.Pontos);
        var linhas = clientes.Select(c => new LinhaPontosCliente(c.Id, c.Nome, c.Telefone, Soma(c.Id, TipoMovimentoPontos.Acumulacao), -Soma(c.Id, TipoMovimentoPontos.Resgate),
                Soma(c.Id, TipoMovimentoPontos.Ajuste), Soma(c.Id, TipoMovimentoPontos.Estorno), c.SaldoPontos))
            .OrderByDescending(l => l.Saldo).ThenByDescending(l => l.Ganhos).Take(MaximoLinhas).ToList();
        return new RelatorioPontos(desde, ate, Soma(null, TipoMovimentoPontos.Acumulacao), -Soma(null, TipoMovimentoPontos.Resgate), Soma(null, TipoMovimentoPontos.Ajuste),
            Soma(null, TipoMovimentoPontos.Estorno), clientes.Sum(c => c.SaldoPontos), clientes.Count(c => c.SaldoPontos > 0), linhas);
    }

    public async Task<RelatorioReservas> ReservasAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var fuso = await config.FusoAsync();
        var agora = relogio.GetUtcNow().UtcDateTime;
        var reservas = await db.Reservas.AsNoTracking().Where(r => r.InicioUtc >= inicio && r.InicioUtc < fim).OrderBy(r => r.InicioUtc).Take(MaximoLinhas + 1)
            .Select(r => new { r.InicioUtc, r.NomeCliente, r.Telefone, r.NumeroPessoas, Mesa = r.Mesa != null ? r.Mesa.Nome : null, r.Estado, r.MotivoCancelamento })
            .ToListAsync(ct);
        var linhas = reservas.Take(MaximoLinhas).Select(r => new LinhaReservaRelatorio(r.InicioUtc, r.NomeCliente, r.Telefone, r.NumeroPessoas, r.Mesa,
            r.Estado switch
            {
                EstadoReserva.Chegada or EstadoReserva.Concluida => SituacaoReserva.Compareceu,
                EstadoReserva.Cancelada => SituacaoReserva.Cancelada,
                // Pendente ou confirmada e a hora já passou (com 1 h de tolerância): o cliente não apareceu.
                _ => r.InicioUtc.AddHours(1) < agora ? SituacaoReserva.Faltou : SituacaoReserva.PorVir,
            }, r.MotivoCancelamento)).ToList();
        int Conta(SituacaoReserva s) => linhas.Count(l => l.Situacao == s);
        var decididas = Conta(SituacaoReserva.Compareceu) + Conta(SituacaoReserva.Faltou);
        var porDia = Enumerable.Range(0, 7).Select(d =>
        {
            var doDia = linhas.Where(l => DiaSemana(TimeZoneInfo.ConvertTimeFromUtc(l.InicioUtc, fuso)) == d).ToList();
            return new LinhaValorQuantidade(NomesDias[d], doDia.Count, doDia.Sum(l => l.Pessoas));
        }).ToList();
        return new RelatorioReservas(desde, ate, linhas.Count, linhas.Where(l => l.Situacao != SituacaoReserva.Cancelada).Sum(l => l.Pessoas), Conta(SituacaoReserva.Compareceu),
            Conta(SituacaoReserva.Faltou), Conta(SituacaoReserva.Cancelada), Conta(SituacaoReserva.PorVir),
            decididas == 0 ? null : Math.Round(100m * Conta(SituacaoReserva.Compareceu) / decididas, 1), porDia, linhas, reservas.Count > MaximoLinhas);
    }
}
