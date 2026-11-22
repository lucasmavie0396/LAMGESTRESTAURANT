using System.Diagnostics;
using System.Net;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Stock;
using LAMGestRestaurant.Domain.Vendas;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Desempenho dos relatórios com um ano de vendas (≈ 150 pedidos por dia). Só corre a pedido, porque demora vários minutos:
/// <c>$env:LAMGEST_TESTES_VOLUME = "1"; dotnet test --filter VolumeRelatorios</c>.
/// Os dados são marcados «DEMO» e ficam só na base de testes (LAMGestRestaurantDB_Tests, recriada em cada execução), num ano
/// antigo (para não mexer nos relatórios de «hoje» dos outros testes). Os tempos aparecem no resultado do teste.
/// </summary>
[Collection(ColeccaoBaseDados.Nome)]
public class VolumeRelatoriosTests(AplicacaoTeste app, ITestOutputHelper saida)
{
    private const int PedidosPorDia = 150;
    /// <summary>Tempo máximo aceitável de cada relatório de um ano (num servidor modesto deve ficar muito abaixo).</summary>
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Relatorios_de_um_ano_respondem_depressa()
    {
        if (Environment.GetEnvironmentVariable("LAMGEST_TESTES_VOLUME") != "1")
        {
            saida.WriteLine("Teste de volume desligado (defina LAMGEST_TESTES_VOLUME=1).");
            return;
        }

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var desde = hoje.AddDays(-800);
        var ate = desde.AddDays(364);
        // Cria o utilizador (e corre o semeador: unidades, armazém, métodos de pagamento) antes de gerar os dados.
        var cliente = await app.ClienteComPermissoesAsync([.. Permissoes.Todas.Keys]);
        var relogio = Stopwatch.StartNew();
        var (pedidos, itens, movimentos) = await GerarAsync(desde, ate);
        saida.WriteLine($"Gerados {pedidos:N0} pedidos, {itens:N0} itens e {movimentos:N0} movimentos de stock DEMO em {relogio.Elapsed.TotalSeconds:N0} s.");

        var periodo = $"desde={desde:yyyy-MM-dd}&ate={ate:yyyy-MM-dd}";
        var mes = $"desde={ate.AddDays(-30):yyyy-MM-dd}&ate={ate:yyyy-MM-dd}";
        (string Nome, string Url)[] relatorios =
        [
            ("Vendas (ecrã)", $"/api/relatorios/vendas?{periodo}"),
            // A lista de vendas recusa mais de 20 000 linhas (pede um período menor): mede-se um mês e a recusa de um ano.
            ("Lista de vendas (1 mês)", $"/api/relatorios/lista-vendas?{mes}"),
            ("Lista de vendas (ano: recusa)", $"/api/relatorios/lista-vendas?{periodo}"),
            ("Clientes", $"/api/relatorios/clientes?{periodo}"),
            ("IVA", $"/api/relatorios/impostos?{periodo}"),
            ("Anulações", $"/api/relatorios/anulacoes?{periodo}"),
            ("Financeiro", $"/api/relatorios/financeiro?{periodo}"),
            ("Stock", $"/api/relatorios/stock?{periodo}"),
            ("Acompanhamentos", $"/api/relatorios/acompanhamentos?{periodo}"),
            ("Painel (dashboard)", "/api/dashboard"),
            ("Documento: vendas", $"/api/relatorios/documento?tipo=Vendas&{periodo}"),
            ("Documento: produtos", $"/api/relatorios/documento?tipo=Produtos&{periodo}"),
            ("Documento: pagamentos", $"/api/relatorios/documento?tipo=Pagamentos&{periodo}"),
            ("Documento: IVA", $"/api/relatorios/documento?tipo=Impostos&{periodo}"),
            ("Documento: lista de vendas (1 mês)", $"/api/relatorios/documento?tipo=ListaVendas&{mes}"),
        ];

        var problemas = new List<string>();
        saida.WriteLine("");
        saida.WriteLine($"{"Relatório",-36} {"1.º (s)",8} {"2.º (s)",8} {"KB",8}  HTTP");
        foreach (var (nome, url) in relatorios)
        {
            var esperado = nome.Contains("recusa", StringComparison.Ordinal) ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK;
            var primeiro = Stopwatch.StartNew();
            var r1 = await cliente.GetAsync(url);
            var corpo1 = await r1.Content.ReadAsStringAsync();
            primeiro.Stop();
            var segundo = Stopwatch.StartNew();
            var r2 = await cliente.GetAsync(url);
            var tamanho = (await r2.Content.ReadAsByteArrayAsync()).Length;
            segundo.Stop();
            saida.WriteLine($"{nome,-36} {primeiro.Elapsed.TotalSeconds,8:N2} {segundo.Elapsed.TotalSeconds,8:N2} {tamanho / 1024,8:N0}  {(int)r1.StatusCode}");
            if (r1.StatusCode != esperado)
                problemas.Add($"{nome}: {(int)r1.StatusCode} {corpo1[..Math.Min(corpo1.Length, 300)]}");
            if (segundo.Elapsed > Limite)
                problemas.Add($"{nome}: {segundo.Elapsed.TotalSeconds:N1} s");
        }
        Assert.Empty(problemas);
    }

    /// <summary>Um ano de vendas DEMO: pedidos pagos (alguns anulados e com itens cancelados), pagamentos e baixas de stock.</summary>
    private async Task<(int Pedidos, int Itens, int Movimentos)> GerarAsync(DateOnly desde, DateOnly ate)
    {
        var aleatorio = new Random(42);
        await using var db = app.CriarContexto();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.Database.SetCommandTimeout(600);

        var un = await db.UnidadesMedida.FirstAsync(u => u.Codigo == "un");
        var kg = await db.UnidadesMedida.FirstOrDefaultAsync(u => u.Codigo == "kg") ?? un;
        var armazem = await db.Armazens.FirstAsync(a => a.Principal);
        var iva = await db.TaxasImposto.FirstAsync(t => t.Percentagem == 16m);
        var metodos = await db.MetodosPagamento.Select(m => m.Id).Take(4).ToListAsync();
        var utilizador = await db.Users.Select(u => u.Id).FirstAsync();

        var marca = Guid.NewGuid().ToString("N")[..6];
        var categorias = Enumerable.Range(1, 10).Select(i => new Categoria { Nome = $"DEMO {marca} categoria {i}" }).ToList();
        db.AddRange(categorias);
        var artigos = Enumerable.Range(1, 40).Select(i => new Artigo
        {
            Codigo = $"DEMO{marca}A{i:000}", Nome = $"DEMO artigo {i}", UnidadeMedidaId = i % 3 == 0 ? kg.Id : un.Id,
            CustoMedio = 10 + i, UltimoCusto = 10 + i, PermiteStockNegativo = true,
        }).ToList();
        db.AddRange(artigos);
        var terminal = new Terminal { Nome = $"DEMO {marca} caixa" };
        db.Add(terminal);
        await db.SaveChangesAsync();

        var produtos = Enumerable.Range(1, 120).Select(i => new Produto
        {
            Codigo = $"DEMO{marca}P{i:000}", Nome = $"DEMO prato {i}", CategoriaId = categorias[i % categorias.Count].Id, Tipo = TipoProduto.Restaurante,
            PrecoVenda = 150 + (i * 7 % 900), Custo = 60 + (i * 3 % 300), TaxaImpostoId = iva.Id,
        }).ToList();
        db.AddRange(produtos);
        var sessao = new SessaoCaixa { TerminalId = terminal.Id, UtilizadorAberturaId = utilizador, AbertaEmUtc = desde.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Estado = EstadoSessaoCaixa.Fechada };
        db.Add(sessao);
        await db.SaveChangesAsync();

        int totalPedidos = 0, totalItens = 0, totalMovimentos = 0, numero = 0;
        for (var dia = desde; dia <= ate; dia = dia.AddDays(5))
        {
            var lote = new List<Pedido>();
            for (var d = dia; d <= ate && d < dia.AddDays(5); d = d.AddDays(1))
            {
                for (var n = 0; n < PedidosPorDia; n++)
                {
                    var fechado = d.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc).AddMinutes(aleatorio.Next(0, 13 * 60));
                    var pedido = new Pedido
                    {
                        Numero = $"DEMO-{marca}-{++numero}", Tipo = (TipoPedido)(n % 3), CriadoEmUtc = fechado.AddMinutes(-40), CriadoPorId = utilizador,
                        SessaoCaixaId = sessao.Id, NumeroPessoas = 2, StockBaixado = true,
                    };
                    if (pedido.Tipo == TipoPedido.Mesa)
                        pedido.Tipo = TipoPedido.Balcao;
                    var linhas = aleatorio.Next(1, 7);
                    for (var l = 0; l < linhas; l++)
                    {
                        var p = produtos[aleatorio.Next(produtos.Count)];
                        var q = aleatorio.Next(1, 4);
                        var total = p.PrecoVenda * q;
                        var imposto = decimal.Round(total * 16m / 116m, 2);
                        var cancelado = aleatorio.Next(40) == 0;
                        pedido.Itens.Add(new PedidoItem
                        {
                            ProdutoId = p.Id, NomeProduto = p.Nome, PrecoUnitario = p.PrecoVenda, TaxaImpostoPercentagem = 16m, Quantidade = q,
                            ValorImposto = imposto, Total = total, EnviaCozinha = true, EstadoCozinha = EstadoItemCozinha.Entregue,
                            EnviadoCozinhaEmUtc = fechado.AddMinutes(-35), CriadoEmUtc = fechado.AddMinutes(-40), Cancelado = cancelado,
                            CanceladoEmUtc = cancelado ? fechado.AddMinutes(-20) : null, MotivoCancelamento = cancelado ? "DEMO" : null,
                        });
                    }
                    var validos = pedido.Itens.Where(i => !i.Cancelado).ToList();
                    pedido.Subtotal = pedido.Total = validos.Sum(i => i.Total);
                    pedido.Impostos = validos.Sum(i => i.ValorImposto);
                    if (aleatorio.Next(60) == 0)
                    {
                        pedido.Estado = EstadoPedido.Cancelado;
                        pedido.CanceladoEmUtc = fechado;
                        pedido.MotivoCancelamento = "DEMO";
                    }
                    else
                    {
                        pedido.Estado = EstadoPedido.Pago;
                        pedido.FechadoEmUtc = fechado;
                        pedido.TotalPago = pedido.Total;
                    }
                    lote.Add(pedido);
                }
            }
            db.AddRange(lote);
            await db.SaveChangesAsync();

            var pagamentos = lote.Where(p => p.Estado == EstadoPedido.Pago && p.Total > 0).Select(p => new Pagamento
            {
                PedidoId = p.Id, SessaoCaixaId = sessao.Id, MetodoPagamentoId = metodos[p.Id % metodos.Count], Valor = p.Total, ValorRecebido = p.Total,
                DataUtc = p.FechadoEmUtc!.Value, UtilizadorId = utilizador, IdempotencyKey = Guid.NewGuid().ToString("N"), CriadoEmUtc = p.FechadoEmUtc.Value,
            }).ToList();
            // Baixa de stock (ficha técnica) em cerca de 60 % dos itens vendidos: 2 artigos por item.
            var movimentos = lote.Where(p => p.Estado == EstadoPedido.Pago).SelectMany(p => p.Itens.Where(i => !i.Cancelado).Select(i => (p, i)))
                .Where(x => x.i.Id % 5 < 3)
                .SelectMany(x => Enumerable.Range(0, 2).Select(k => new MovimentoStock
                {
                    ArtigoId = artigos[(x.i.ProdutoId + k * 7) % artigos.Count].Id, ArmazemId = armazem.Id, Tipo = TipoMovimentoStock.SaidaVenda,
                    Quantidade = -0.25m * x.i.Quantidade, CustoUnitario = 12m + k, OrigemTipo = ServicoBaixaStock.OrigemPedidoItem, OrigemId = x.i.Id,
                    UtilizadorId = utilizador, DataUtc = x.p.FechadoEmUtc!.Value,
                })).ToList();
            db.AddRange(pagamentos);
            db.AddRange(movimentos);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            totalPedidos += lote.Count;
            totalItens += lote.Sum(p => p.Itens.Count);
            totalMovimentos += movimentos.Count;
        }
        return (totalPedidos, totalItens, totalMovimentos);
    }
}
