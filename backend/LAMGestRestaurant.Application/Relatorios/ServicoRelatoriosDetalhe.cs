using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Relatorios;

// ---------- Lista de vendas (pedido a pedido) ----------

/// <summary>
/// Estado: Pago (por omissão, as vendas válidas, pela data do pagamento), Estornado ou Cancelado (pela data do estorno/cancelamento).
/// Horas (locais) opcionais: só os pedidos dessa janela em cada dia. Todas = sem paginação (exportação), até 20 000 linhas.
/// </summary>
public record FiltroListaVendas(
    DateOnly Desde, DateOnly Ate, TimeOnly? HoraDe = null, TimeOnly? HoraAte = null, EstadoPedido? Estado = null, TipoPedido? Tipo = null,
    int? OperadorId = null, int? ClienteId = null, int? MetodoPagamentoId = null, string? Pesquisa = null, int Pagina = 1, int Tamanho = 50, bool Todas = false);

public record LinhaVendaRelatorio(
    int Id, string Numero, DateTime DataUtc, TipoPedido Tipo, string? Mesa, string? Cliente, string? Operador, string? Garcom,
    decimal Subtotal, decimal Desconto, decimal Impostos, decimal TaxaEntrega, decimal Total, decimal Recebido, decimal Troco,
    string Metodos, EstadoPedido Estado, string? Recibo, string? Motivo);

public record TotaisListaVendas(int Vendas, decimal Subtotal, decimal Desconto, decimal Impostos, decimal TaxaEntrega, decimal Total, decimal Recebido, decimal Troco);

public record OpcaoFiltro(int Id, string Nome);

public record ListaVendas(Pagina<LinhaVendaRelatorio> Vendas, TotaisListaVendas Totais, IReadOnlyList<OpcaoFiltro> Operadores, IReadOnlyList<OpcaoFiltro> Metodos);

// ---------- Por cliente ----------

public record VendaCliente(int ClienteId, string Nome, string? Telefone, string? Cartao, int Compras, decimal Total, decimal Descontos, DateTime UltimaCompraUtc);

public record RelatorioClientes(DateOnly Desde, DateOnly Ate, IReadOnlyList<VendaCliente> Clientes, int VendasComCliente, decimal TotalComCliente,
    int VendasSemCliente, decimal TotalSemCliente);

// ---------- IVA / impostos e descontos ----------

public record ImpostoTaxa(decimal Percentagem, string Taxa, decimal BaseSemImposto, decimal Imposto, decimal ComImposto);

public record ImpostoPeriodo(string Periodo, int Vendas, decimal Descontos, decimal BaseSemImposto, decimal Imposto, decimal TaxasEntrega, decimal Total);

public record RelatorioImpostos(DateOnly Desde, DateOnly Ate, string Agrupamento, int Vendas, decimal Subtotal, decimal Descontos,
    decimal BaseSemImposto, decimal Impostos, decimal TaxasEntrega, decimal Total, IReadOnlyList<ImpostoTaxa> PorTaxa, IReadOnlyList<ImpostoPeriodo> PorPeriodo);

// ---------- Anulações e devoluções ----------

public record PedidoAnulado(int Id, string Numero, DateTime DataUtc, TipoPedido Tipo, string? Mesa, string? Por, string? Motivo, decimal Valor, string? Recibo);

public record ItemAnulado(int PedidoId, string Pedido, DateTime DataUtc, string Produto, decimal Quantidade, decimal Valor, string? Por, string? Motivo);

public record PagamentoAnulado(int PedidoId, string Pedido, DateTime DataUtc, string Metodo, decimal Valor, string? Por, string? Motivo);

public record RelatorioAnulacoes(DateOnly Desde, DateOnly Ate, IReadOnlyList<PedidoAnulado> PedidosCancelados, IReadOnlyList<PedidoAnulado> Estornos,
    IReadOnlyList<ItemAnulado> ItensCancelados, IReadOnlyList<PagamentoAnulado> PagamentosAnulados);

/// <summary>
/// Relatórios de detalhe (só leitura): lista de vendas, por cliente, impostos e descontos, anulações e devoluções.
/// Mesmas regras dos resumos: uma venda é um pedido Pago, na data local (fuso configurado) em que fechou; estornos e
/// cancelamentos nunca contam como facturação e aparecem à parte, pela data em que aconteceram.
/// </summary>
public class ServicoRelatoriosDetalhe(IAppDbContext db, IUtilizadorActual actual, IConsultaUtilizadores utilizadores, ConfiguracoesSistema config)
{
    public const int MaximoLinhas = 20_000;

    public async Task<ListaVendas> ListaVendasAsync(FiltroListaVendas f, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(f.Desde, f.Ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(f.Desde, f.Ate);
        var fuso = await config.FusoAsync();
        var estado = f.Estado ?? EstadoPedido.Pago;
        if (estado == EstadoPedido.Aberto)
            throw new RegraNegocioException("Pedidos abertos ainda não são vendas: use o POS ou o painel.");

        // Pago: data do pagamento; estornado/cancelado: data do estorno/cancelamento.
        var baseConsulta = db.Pedidos.AsNoTracking().Where(p => p.Estado == estado);
        baseConsulta = estado == EstadoPedido.Pago
            ? baseConsulta.Where(p => p.FechadoEmUtc >= inicio && p.FechadoEmUtc < fim)
            : baseConsulta.Where(p => p.CanceladoEmUtc >= inicio && p.CanceladoEmUtc < fim);
        if (f.Tipo is TipoPedido tipo) baseConsulta = baseConsulta.Where(p => p.Tipo == tipo);
        if (f.ClienteId is int cliente) baseConsulta = baseConsulta.Where(p => p.ClienteId == cliente);
        if (f.MetodoPagamentoId is int metodo)
            baseConsulta = baseConsulta.Where(p => db.Pagamentos.Any(pg => pg.PedidoId == p.Id && pg.Estado == EstadoPagamento.Valido && pg.MetodoPagamentoId == metodo));
        if (!string.IsNullOrWhiteSpace(f.Pesquisa))
        {
            var t = f.Pesquisa.Trim();
            baseConsulta = baseConsulta.Where(p => p.Numero.Contains(t) || (p.Cliente != null && p.Cliente.Nome.Contains(t))
                || (p.ContactoEntrega != null && p.ContactoEntrega.Contains(t)) || (p.Mesa != null && p.Mesa.Nome == t));
        }
        // Operadores possíveis com os restantes filtros (para a lista de escolha).
        var idsOperadores = await baseConsulta.Where(p => p.CriadoPorId != null).Select(p => p.CriadoPorId!.Value).Distinct().ToListAsync(ct);
        var consulta = f.OperadorId is int operador ? baseConsulta.Where(p => p.CriadoPorId == operador) : baseConsulta;

        if (await consulta.CountAsync(ct) > MaximoLinhas)
            throw new RegraNegocioException($"Mais de {MaximoLinhas:N0} vendas com estes filtros: reduza o período ou use mais filtros.");
        var linhas = await consulta
            .Select(p => new
            {
                p.Id, p.Numero, Data = estado == EstadoPedido.Pago ? p.FechadoEmUtc!.Value : p.CanceladoEmUtc!.Value, p.Tipo,
                Mesa = p.Mesa != null ? p.Mesa.Nome : null, Cliente = p.Cliente != null ? p.Cliente.Nome : null, p.CriadoPorId,
                Garcom = p.Garcom != null ? p.Garcom.Nome : null, p.Subtotal, p.Desconto, p.Impostos, p.TaxaEntrega, p.Total, p.Estado,
                Recibo = db.Recibos.Where(r => r.PedidoId == p.Id).OrderBy(r => r.Anulado).Select(r => r.Numero).FirstOrDefault(),
                p.MotivoCancelamento,
            })
            .ToListAsync(ct);

        // Janela de horas locais (ex.: 12:00–15:00 em todos os dias do período).
        if (f.HoraDe is not null || f.HoraAte is not null)
        {
            var de = f.HoraDe ?? TimeOnly.MinValue;
            var ate = f.HoraAte ?? TimeOnly.MaxValue;
            linhas = [.. linhas.Where(l =>
            {
                var hora = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(l.Data, fuso));
                return de <= ate ? hora >= de && hora <= ate : hora >= de || hora <= ate; // janela que passa a meia-noite
            })];
        }
        linhas = [.. linhas.OrderBy(l => l.Data).ThenBy(l => l.Id)];

        // Pagamentos válidos dos pedidos da lista (recebido, troco e métodos), numa só consulta.
        var pagamentos = await db.Pagamentos.AsNoTracking()
            .Where(pg => pg.Estado == EstadoPagamento.Valido && consulta.Select(p => p.Id).Contains(pg.PedidoId))
            .OrderBy(pg => pg.Id) // métodos pela ordem em que foram registados
            .Select(pg => new { pg.PedidoId, pg.ValorRecebido, pg.Troco, Metodo = pg.MetodoPagamento.Nome })
            .ToListAsync(ct);
        var porPedido = pagamentos.GroupBy(pg => pg.PedidoId).ToDictionary(g => g.Key,
            g => (Recebido: g.Sum(x => x.ValorRecebido), Troco: g.Sum(x => x.Troco), Metodos: string.Join(", ", g.Select(x => x.Metodo).Distinct())));
        var nomes = await utilizadores.NomesAsync(idsOperadores, ct);

        var todas = linhas.Select(l =>
        {
            var pg = porPedido.GetValueOrDefault(l.Id);
            return new LinhaVendaRelatorio(l.Id, l.Numero, l.Data, l.Tipo, l.Mesa, l.Cliente,
                l.CriadoPorId is int u ? nomes.GetValueOrDefault(u) : null, l.Garcom,
                l.Subtotal, l.Desconto, l.Impostos, l.TaxaEntrega, l.Total, pg.Recebido, pg.Troco, pg.Metodos ?? "",
                l.Estado, l.Recibo, l.MotivoCancelamento);
        }).ToList();

        var totais = new TotaisListaVendas(todas.Count, todas.Sum(v => v.Subtotal), todas.Sum(v => v.Desconto), todas.Sum(v => v.Impostos),
            todas.Sum(v => v.TaxaEntrega), todas.Sum(v => v.Total), todas.Sum(v => v.Recebido), todas.Sum(v => v.Troco));
        var (pagina, tamanho) = f.Todas ? (1, Math.Max(1, todas.Count)) : Pagina<LinhaVendaRelatorio>.Normalizar(f.Pagina, f.Tamanho);
        var metodos = await db.MetodosPagamento.AsNoTracking().OrderBy(m => m.Ordem).Select(m => new OpcaoFiltro(m.Id, m.Nome)).ToListAsync(ct);

        return new ListaVendas(
            new Pagina<LinhaVendaRelatorio>([.. todas.Skip((pagina - 1) * tamanho).Take(tamanho)], todas.Count, pagina, tamanho),
            totais,
            [.. idsOperadores.Select(id => new OpcaoFiltro(id, nomes.GetValueOrDefault(id, "?"))).OrderBy(o => o.Nome)],
            metodos);
    }

    /// <summary>Vendas por cliente associado no POS. Telefone só para quem pode ver clientes.</summary>
    public async Task<RelatorioClientes> ClientesAsync(DateOnly desde, DateOnly ate, string? pesquisa, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var pagos = db.Pedidos.AsNoTracking().Where(p => p.Estado == EstadoPedido.Pago && p.FechadoEmUtc >= inicio && p.FechadoEmUtc < fim);

        var semCliente = await pagos.Where(p => p.ClienteId == null)
            .GroupBy(_ => 1).Select(g => new { Vendas = g.Count(), Total = g.Sum(p => p.Total) }).FirstOrDefaultAsync(ct);
        var comCliente = pagos.Where(p => p.ClienteId != null);
        if (!string.IsNullOrWhiteSpace(pesquisa))
        {
            var t = pesquisa.Trim();
            comCliente = comCliente.Where(p => p.Cliente!.Nome.Contains(t) || (p.Cliente.Telefone != null && p.Cliente.Telefone.Contains(t))
                || (p.Cliente.CartaoFidelidade != null && p.Cliente.CartaoFidelidade.Contains(t)));
        }
        var grupos = await comCliente
            .GroupBy(p => new { Id = p.ClienteId!.Value, p.Cliente!.Nome, p.Cliente.Telefone, p.Cliente.CartaoFidelidade })
            .Select(g => new { g.Key, Compras = g.Count(), Total = g.Sum(p => p.Total), Descontos = g.Sum(p => p.Desconto), Ultima = g.Max(p => p.FechadoEmUtc!.Value) })
            .ToListAsync(ct);
        var verContactos = actual.TemPermissao(Permissoes.ClientesVer);
        var clientes = grupos
            .Select(g => new VendaCliente(g.Key.Id, g.Key.Nome, verContactos ? g.Key.Telefone : null, g.Key.CartaoFidelidade, g.Compras, g.Total, g.Descontos, g.Ultima))
            .OrderByDescending(c => c.Total).ThenBy(c => c.Nome).ToList();

        return new RelatorioClientes(desde, ate, clientes, clientes.Sum(c => c.Compras), clientes.Sum(c => c.Total),
            semCliente?.Vendas ?? 0, semCliente?.Total ?? 0m);
    }

    /// <summary>
    /// Imposto incluído por taxa e por dia (ou mês). Por pedido, o desconto reduz proporcionalmente o imposto (como no cálculo
    /// do pedido) e os arredondamentos de cada taxa são acertados para a soma bater com o imposto gravado no pedido.
    /// A taxa de entrega não leva imposto.
    /// </summary>
    public async Task<RelatorioImpostos> ImpostosAsync(DateOnly desde, DateOnly ate, string? agrupar, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var fuso = await config.FusoAsync();
        var porMes = agrupar switch { "mes" => true, "dia" => false, _ => ate.DayNumber - desde.DayNumber > 62 };

        var pedidos = await db.Pedidos.AsNoTracking()
            .Where(p => p.Estado == EstadoPedido.Pago && p.FechadoEmUtc >= inicio && p.FechadoEmUtc < fim)
            .Select(p => new { p.Id, Fechado = p.FechadoEmUtc!.Value, p.Subtotal, p.Desconto, p.Impostos, p.TaxaEntrega, p.Total })
            .ToListAsync(ct);
        var itens = (await db.PedidoItens.AsNoTracking()
                .Where(i => !i.Cancelado && i.Pedido.Estado == EstadoPedido.Pago && i.Pedido.FechadoEmUtc >= inicio && i.Pedido.FechadoEmUtc < fim)
                .Select(i => new { i.PedidoId, i.TaxaImpostoPercentagem, i.Total, i.ValorImposto })
                .ToListAsync(ct))
            .ToLookup(i => i.PedidoId);

        // Por pedido e taxa: valor com imposto e imposto, depois do desconto do pedido.
        var linhas = new List<(string Periodo, decimal Taxa, decimal Com, decimal Imposto)>();
        foreach (var p in pedidos)
        {
            var periodo = Periodo(p.Fechado);
            var factor = p.Subtotal == 0 ? 0m : (p.Subtotal - p.Desconto) / p.Subtotal;
            var porTaxa = itens[p.Id].GroupBy(i => i.TaxaImpostoPercentagem)
                .Select(g => (Taxa: g.Key, Com: CalculoPedido.Arredondar(g.Sum(i => i.Total) * factor), Imposto: CalculoPedido.Arredondar(g.Sum(i => i.ValorImposto) * factor)))
                .OrderByDescending(x => x.Com).ToList();
            if (porTaxa.Count == 0)
                continue;
            // Acerto dos cêntimos na taxa de maior valor: as somas batem com o pedido.
            var maior = porTaxa[0];
            porTaxa[0] = (maior.Taxa, maior.Com + (p.Subtotal - p.Desconto - porTaxa.Sum(x => x.Com)), maior.Imposto + (p.Impostos - porTaxa.Sum(x => x.Imposto)));
            linhas.AddRange(porTaxa.Select(x => (periodo, x.Taxa, x.Com, x.Imposto)));
        }

        var taxas = linhas.GroupBy(l => l.Taxa).OrderByDescending(g => g.Key)
            .Select(g => new ImpostoTaxa(g.Key, g.Key == 0 ? "Isento (0%)" : $"IVA {g.Key:0.##}%", g.Sum(l => l.Com - l.Imposto), g.Sum(l => l.Imposto), g.Sum(l => l.Com)))
            .ToList();
        var periodos = pedidos.GroupBy(p => Periodo(p.Fechado)).OrderBy(g => g.Key)
            .Select(g => new ImpostoPeriodo(g.Key, g.Count(), g.Sum(p => p.Desconto),
                g.Sum(p => p.Total - p.Impostos - p.TaxaEntrega), g.Sum(p => p.Impostos), g.Sum(p => p.TaxaEntrega), g.Sum(p => p.Total)))
            .ToList();

        var total = pedidos.Sum(p => p.Total);
        var impostos = pedidos.Sum(p => p.Impostos);
        var entrega = pedidos.Sum(p => p.TaxaEntrega);
        return new RelatorioImpostos(desde, ate, porMes ? "mes" : "dia", pedidos.Count, pedidos.Sum(p => p.Subtotal), pedidos.Sum(p => p.Desconto),
            total - impostos - entrega, impostos, entrega, total, taxas, periodos);

        string Periodo(DateTime utc)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, fuso);
            return porMes ? local.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)
                : local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Pedidos cancelados, estornos (devoluções), itens cancelados depois de enviados e pagamentos anulados, pela data em que
    /// aconteceram. Nenhum conta como facturação. O valor de um pedido cancelado é o dos seus itens no momento do cancelamento,
    /// antes do desconto e sem os retirados antes do envio (nunca chegaram a ser pedidos), como no resumo de vendas.
    /// </summary>
    public async Task<RelatorioAnulacoes> AnulacoesAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);

        var anulados = await db.Pedidos.AsNoTracking()
            .Where(p => (p.Estado == EstadoPedido.Cancelado || p.Estado == EstadoPedido.Estornado) && p.CanceladoEmUtc >= inicio && p.CanceladoEmUtc < fim)
            .OrderBy(p => p.CanceladoEmUtc)
            .Select(p => new
            {
                p.Id, p.Numero, Data = p.CanceladoEmUtc!.Value, p.Tipo, Mesa = p.Mesa != null ? p.Mesa.Nome : null, p.CanceladoPorId, p.MotivoCancelamento,
                p.Estado, p.TotalPago,
                ValorEnviado = p.Itens.Where(i => i.MotivoCancelamento != ServicoPedidos.MotivoRemovidoAntesEnvio).Sum(i => (decimal?)i.Total) ?? 0m,
                Recibo = db.Recibos.Where(r => r.PedidoId == p.Id).Select(r => r.Numero).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var itens = await db.PedidoItens.AsNoTracking()
            .Where(i => i.Cancelado && i.EstadoCozinha != EstadoItemCozinha.NaoEnviado && i.CanceladoEmUtc >= inicio && i.CanceladoEmUtc < fim
                        && i.Pedido.Estado != EstadoPedido.Cancelado)
            .OrderBy(i => i.CanceladoEmUtc)
            .Select(i => new { i.PedidoId, Pedido = i.Pedido.Numero, Data = i.CanceladoEmUtc!.Value, i.NomeProduto, i.Quantidade, i.Total, i.CanceladoPorId, i.MotivoCancelamento })
            .ToListAsync(ct);
        var pagamentos = await db.Pagamentos.AsNoTracking()
            .Where(p => p.Estado == EstadoPagamento.Anulado && p.AnuladoEmUtc >= inicio && p.AnuladoEmUtc < fim)
            .OrderBy(p => p.AnuladoEmUtc)
            .Select(p => new { p.PedidoId, Pedido = p.Pedido.Numero, Data = p.AnuladoEmUtc!.Value, Metodo = p.MetodoPagamento.Nome, p.Valor, p.AnuladoPorId, p.MotivoAnulacao })
            .ToListAsync(ct);

        var nomes = await utilizadores.NomesAsync(
            anulados.Select(a => a.CanceladoPorId).Concat(itens.Select(i => i.CanceladoPorId)).Concat(pagamentos.Select(p => p.AnuladoPorId))
                .Where(id => id is not null).Select(id => id!.Value).Distinct(), ct);
        string? Nome(int? id) => id is int u ? nomes.GetValueOrDefault(u) : null;

        return new RelatorioAnulacoes(desde, ate,
            [.. anulados.Where(a => a.Estado == EstadoPedido.Cancelado)
                .Select(a => new PedidoAnulado(a.Id, a.Numero, a.Data, a.Tipo, a.Mesa, Nome(a.CanceladoPorId), a.MotivoCancelamento, a.ValorEnviado, null))],
            [.. anulados.Where(a => a.Estado == EstadoPedido.Estornado)
                .Select(a => new PedidoAnulado(a.Id, a.Numero, a.Data, a.Tipo, a.Mesa, Nome(a.CanceladoPorId), a.MotivoCancelamento, a.TotalPago, a.Recibo))],
            [.. itens.Select(i => new ItemAnulado(i.PedidoId, i.Pedido, i.Data, i.NomeProduto, i.Quantidade, i.Total, Nome(i.CanceladoPorId), i.MotivoCancelamento))],
            [.. pagamentos.Select(p => new PagamentoAnulado(p.PedidoId, p.Pedido, p.Data, p.Metodo, p.Valor, Nome(p.AnuladoPorId), p.MotivoAnulacao))]);
    }
}
