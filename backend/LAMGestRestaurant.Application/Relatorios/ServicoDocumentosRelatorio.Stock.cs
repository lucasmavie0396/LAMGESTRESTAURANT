using System.Globalization;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Relatorios;

public partial class ServicoDocumentosRelatorio
{
    private async Task<DocumentoImpressao> ExistenciasAsync(int? armazemId, int? categoriaId, CancellationToken ct)
    {
        var r = await gestao.ExistenciasAsync(armazemId, categoriaId, ct);
        var l = await CabecalhoAsync("Existências por armazém", $"{(r.Armazem is null ? "Todos os armazéns" : r.Armazem)} · em {r.Data:dd/MM/yyyy}");
        l.Add(LinhaImpressao.NovoCartao("Valor total", Valor(r.ValorTotal)));
        l.Add(LinhaImpressao.NovoCartao("Linhas", Inteiro(r.Linhas.Count)));
        l.Add(LinhaImpressao.NovoCartao("Abaixo do mínimo", Inteiro(r.AbaixoMinimo), alerta: r.AbaixoMinimo > 0));
        if (r.Linhas.Count == 0) l.Add(new("Sem existências."));
        foreach (var armazem in r.Linhas.GroupBy(x => x.Armazem))
            Grelha(l, armazem.Key, Resumo(armazem.Count(), "artigo", armazem.Sum(x => x.Valor)), ["Código", "Artigo", "Localização", "Quantidade", "Mínimo", "Valor"],
                armazem.Select(a => Lin(a.AbaixoMinimo, a.Codigo, a.Artigo, a.Localizacao ?? "", $"{ComposicaoImpressao.Quantidade(a.Quantidade)} {a.Unidade}",
                    a.Minimo > 0 ? $"{ComposicaoImpressao.Quantidade(a.Minimo)} {a.Unidade}" : "—", Valor(a.Valor))),
                subtotal: [$"Subtotal {armazem.Key}", "", "", "", "", Valor(armazem.Sum(x => x.Valor))], texto: 3);
        l.Add(new("VALOR TOTAL", Valor(r.ValorTotal), Negrito: true, Grande: true));
        Legenda(l, "abaixo do stock mínimo");
        Rodape(l);
        return new DocumentoImpressao($"Existências {r.Data:dd-MM-yyyy}", l, cabecalho);
    }

    private async Task<DocumentoImpressao> TransferenciasAsync(DateOnly desde, DateOnly ate, int? armazemId, CancellationToken ct)
    {
        var r = await gestao.TransferenciasAsync(desde, ate, armazemId, ct);
        var l = await CabecalhoAsync("Transferências entre armazéns", $"{Periodo(desde, ate)}{(r.Armazem is null ? "" : $" · {r.Armazem}")}");
        l.Add(LinhaImpressao.NovoCartao("Transferências", Inteiro(r.Linhas.Count)));
        l.Add(LinhaImpressao.NovoCartao("Valor transferido", Valor(r.Linhas.Sum(t => t.Valor))));
        l.Add(LinhaImpressao.NovoCartao("Percursos", Inteiro(r.PorPercurso.Count)));
        Grelha(l, "Transferências", Resumo(r.Linhas.Count, "transferência", r.Linhas.Sum(t => t.Valor)),
            ["Data", "Artigo", "De → Para", "Por", "Quantidade", "Valor"],
            r.Linhas.Select(t => Lin($"{TimeZoneInfo.ConvertTimeFromUtc(t.DataUtc, fuso):dd/MM HH:mm}", t.Artigo, $"{t.Origem} → {t.Destino}", t.Utilizador ?? "—",
                $"{ComposicaoImpressao.Quantidade(t.Quantidade)} {t.Unidade}", Valor(t.Valor))),
            subtotal: ["Total", "", "", "", "", Valor(r.Linhas.Sum(t => t.Valor))], texto: 4, vazio: "Sem transferências no período.");
        if (r.PorPercurso.Count > 0)
            Grelha(l, "Por percurso", null, ["Percurso", "Transferências", "Valor"],
                r.PorPercurso.Select(p => Lin(p.Rotulo, Inteiro(p.Quantidade), Valor(p.Valor))));
        Rodape(l);
        return new DocumentoImpressao("Transferências " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> StockAsync(CancellationToken ct)
    {
        var artigos = await db.Artigos.AsNoTracking().Where(a => a.Activo)
            .OrderBy(a => a.Tipo).ThenBy(a => a.Nome)
            .Select(a => new
            {
                a.Codigo, a.Nome, a.Tipo, Unidade = a.UnidadeMedida.Codigo, a.CustoMedio, a.StockMinimo,
                Saldo = db.StockSaldos.Where(s => s.ArtigoId == a.Id).Sum(s => (decimal?)s.Quantidade) ?? 0m,
            })
            .ToListAsync(ct);
        var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, fuso));
        decimal ValorDe(decimal saldo, decimal custo) => Math.Round(Math.Max(0, saldo) * custo, 2);
        var total = artigos.Sum(a => ValorDe(a.Saldo, a.CustoMedio));
        var abaixo = artigos.Count(a => a.StockMinimo > 0 && a.Saldo <= a.StockMinimo);
        var negativos = artigos.Count(a => a.Saldo < 0);

        var l = await CabecalhoAsync("Existências em stock", $"Em {hoje:dd/MM/yyyy}");
        l.Add(LinhaImpressao.NovoCartao("Valor do stock", Valor(total)));
        l.Add(LinhaImpressao.NovoCartao("Artigos", Inteiro(artigos.Count)));
        l.Add(LinhaImpressao.NovoCartao("Abaixo do mínimo", Inteiro(abaixo), alerta: abaixo > 0));
        l.Add(LinhaImpressao.NovoCartao("Stock negativo", Inteiro(negativos), alerta: negativos > 0));
        foreach (var tipo in artigos.GroupBy(a => a.Tipo))
        {
            var subtotal = tipo.Sum(a => ValorDe(a.Saldo, a.CustoMedio));
            Grelha(l, NomeTipo(tipo.Key), Resumo(tipo.Count(), "artigo", subtotal), ["Código", "Artigo", "Quantidade", "Mínimo", "Custo médio", "Valor"],
                tipo.Select(a => Lin(a.StockMinimo > 0 && a.Saldo <= a.StockMinimo, a.Codigo, a.Nome, $"{ComposicaoImpressao.Quantidade(a.Saldo)} {a.Unidade}",
                    a.StockMinimo > 0 ? $"{ComposicaoImpressao.Quantidade(a.StockMinimo)} {a.Unidade}" : "—", $"{Valor(a.CustoMedio)}/{a.Unidade}",
                    Valor(ValorDe(a.Saldo, a.CustoMedio)))),
                subtotal: [$"Subtotal {NomeTipo(tipo.Key)}", "", "", "", "", Valor(subtotal)], texto: 2);
        }
        if (artigos.Count == 0) l.Add(new("Sem artigos de stock."));
        l.Add(new("VALOR TOTAL DO STOCK", Valor(total), Negrito: true, Grande: true));
        Legenda(l, "abaixo do stock mínimo. Quantidades negativas não somam ao valor");
        Rodape(l);
        return new DocumentoImpressao($"Stock {hoje:dd-MM-yyyy}", l, cabecalho);
    }

    /// <summary>
    /// Folha de contagem de um inventário, para contar no armazém em papel: artigo, unidade e espaço para a quantidade contada.
    /// Ordem: alfabética, por código, por categoria e subcategoria, por tipo de artigo ou por localização no armazém (com títulos
    /// por grupo). Opções: só algumas categorias (as subcategorias entram), uma página por grupo (A4), esconder os de saldo zero.
    /// Por omissão é cega (sem o saldo do sistema, para não influenciar quem conta). A contagem escreve-se depois no ecrã do inventário.
    /// </summary>
    private async Task<DocumentoImpressao> FolhaContagemAsync(int inventarioId, OpcoesFolhaContagem o)
    {
        var inv = await stock.ObterInventarioAsync(inventarioId);
        IEnumerable<Stock.InventarioLinhaDto> linhas = inv.Linhas;
        if (o.Categorias is { Count: > 0 } escolhidas)
        {
            var filhas = await db.Categorias.AsNoTracking().Where(c => c.CategoriaPaiId != null && escolhidas.Contains(c.CategoriaPaiId.Value)).Select(c => c.Id).ToListAsync();
            var ids = escolhidas.Concat(filhas).ToHashSet();
            linhas = linhas.Where(x => x.CategoriaId is int c && ids.Contains(c));
        }
        if (o.EsconderZero) linhas = linhas.Where(x => x.QuantidadeSistema != 0);
        var lista = linhas.ToList();

        var l = await CabecalhoAsync("Folha de contagem", $"Inventário n.º {inv.Id} · {inv.Armazem}");
        var dados = new List<LinhaImpressao>
        {
            Lin("Criado em", DataHora(inv.CriadoEmUtc)),
            Lin("Ordem", $"{NomeOrdem(o.Ordem)}{(o.Categorias is { Count: > 0 } ? " · só as categorias escolhidas" : "")}{(o.EsconderZero ? " · sem os de saldo zero" : "")}"),
            Lin("Artigos", Inteiro(lista.Count)),
        };
        if (inv.Estado != EstadoInventario.Aberto)
            dados.Add(LinhaImpressao.Tabela(["Estado", $"INVENTÁRIO {inv.Estado.ToString().ToUpperInvariant()}"], alerta: true));
        Grelha(l, "Inventário", null, ["Descrição", "Valor"], dados);
        l.Add(new(o.ComSaldo ? "Com o saldo do sistema." : "Contagem cega: conte e escreva a quantidade de cada artigo."));

        const string espaco = "__________";
        var porNome = StringComparer.CurrentCultureIgnoreCase;
        void Tabela(string titulo, IReadOnlyCollection<Stock.InventarioLinhaDto> artigos, bool quebra = false)
        {
            Grelha(l, titulo, $"{artigos.Count} artigos", o.ComSaldo ? ["Código", "Artigo", "Un.", "Sistema", "Contado"] : ["Código", "Artigo", "Un.", "Contado"],
                artigos.Select(x => LinhaImpressao.Tabela(o.ComSaldo
                    ? [x.Codigo, x.Artigo, x.Unidade, ComposicaoImpressao.Quantidade(x.QuantidadeSistema), espaco]
                    : [x.Codigo, x.Artigo, x.Unidade, espaco])), texto: 3);
            if (quebra) l[l.FindLastIndex(x => x.Faixa)] = l[l.FindLastIndex(x => x.Faixa)] with { QuebraPagina = true };
        }
        IOrderedEnumerable<Stock.InventarioLinhaDto> Alfabetica(IEnumerable<Stock.InventarioLinhaDto> a) => a.OrderBy(x => x.Artigo, porNome).ThenBy(x => x.Codigo);

        // Grupos (título, subgrupos com título opcional): «sem categoria/localização» ficam no fim.
        var grupos = o.Ordem switch
        {
            OrdemFolhaContagem.Categoria => lista.GroupBy(x => x.Categoria).OrderBy(g => g.Key is null).ThenBy(g => g.Key, porNome)
                .Select(g => (Titulo: g.Key ?? "Sem categoria", Sub: g.GroupBy(x => x.Subcategoria).OrderBy(s => s.Key is not null).ThenBy(s => s.Key, porNome)
                    .Select(s => (Titulo: s.Key, Artigos: Alfabetica(s).ToList())).ToList())).ToList(),
            OrdemFolhaContagem.Tipo => lista.GroupBy(x => x.Tipo).OrderBy(g => g.Key)
                .Select(g => (Titulo: NomeTipo(g.Key), Sub: new List<(string? Titulo, List<Stock.InventarioLinhaDto> Artigos)> { (null, Alfabetica(g).ToList()) })).ToList(),
            OrdemFolhaContagem.Localizacao => lista.GroupBy(x => x.Localizacao).OrderBy(g => g.Key is null).ThenBy(g => g.Key, porNome)
                .Select(g => (Titulo: g.Key ?? "Sem localização", Sub: new List<(string? Titulo, List<Stock.InventarioLinhaDto> Artigos)> { (null, Alfabetica(g).ToList()) })).ToList(),
            _ => [],
        };
        if (grupos.Count == 0 && lista.Count > 0)
            Tabela("Artigos", [.. o.Ordem == OrdemFolhaContagem.Codigo ? lista.OrderBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase) : Alfabetica(lista)]);
        for (var i = 0; i < grupos.Count; i++)
        {
            var (titulo, subs) = grupos[i];
            var total = subs.Sum(s => s.Artigos.Count);
            var quebra = o.PaginaPorGrupo && i > 0;
            // Um só subgrupo sem título: a grelha tem o título do grupo; com subcategorias, uma grelha por subcategoria («Grupo › Sub»).
            if (subs is [(null, var unicos)])
                Tabela($"{titulo.ToUpperInvariant()} ({total})", unicos, quebra);
            else
                for (var k = 0; k < subs.Count; k++)
                    Tabela(subs[k].Titulo is null ? $"{titulo.ToUpperInvariant()} ({total})" : $"{titulo.ToUpperInvariant()} › {subs[k].Titulo} ({subs[k].Artigos.Count})", subs[k].Artigos, quebra && k == 0);
            if (o.PaginaPorGrupo)
            {
                l.Add(new(""));
                l.Add(new("Contado por: __________________   Data: ____/____"));
            }
        }
        if (lista.Count == 0) l.Add(new(inv.Linhas.Count == 0 ? "Sem artigos activos neste armazém." : "Nenhum artigo com estas opções."));
        l.Add(LinhaImpressao.Traco);
        l.Add(new("Data da contagem: ____/____/________"));
        Assinaturas(l, "Contado por", "Conferido por");
        Rodape(l);
        return new DocumentoImpressao($"Contagem inventário {inv.Id}", l, cabecalho);
    }

    private static string NomeOrdem(OrdemFolhaContagem ordem) => ordem switch
    {
        OrdemFolhaContagem.Codigo => "por código",
        OrdemFolhaContagem.Categoria => "por categoria e subcategoria",
        OrdemFolhaContagem.Tipo => "por tipo de artigo",
        OrdemFolhaContagem.Localizacao => "por localização no armazém",
        _ => "alfabética",
    };

    private static string NomeTipo(TipoArtigo tipo) => tipo switch
    {
        TipoArtigo.Ingrediente => "Ingredientes",
        TipoArtigo.Revenda => "Revenda",
        _ => "Consumíveis",
    };
}
