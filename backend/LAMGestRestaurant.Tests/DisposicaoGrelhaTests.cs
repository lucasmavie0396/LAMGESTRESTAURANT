using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Infrastructure.Impressao;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Relatórios em grelha (impressão directa e PDF): em A4 título, quadros de resumo, faixa escura por grelha, colunas de texto,
/// alertas a vermelho, subtotal a ocupar as colunas de texto e total num quadro; nos talões tudo compacto e a preto.
/// </summary>
public class DisposicaoGrelhaTests
{
    private sealed class Medidor : IMedidorTexto
    {
        public double Largura(string texto, EstiloTexto estilo) => texto.Length * 1.8;
        public double Altura(EstiloTexto estilo) => estilo == EstiloTexto.Grande ? 5 : 4;
    }

    private static DocumentoImpressao Stock(int artigos = 2) => new("Stock",
    [
        new("EXISTÊNCIAS EM STOCK", "Em 18/11/2026", Alinhamento.Centro, Negrito: true, Titulo: true),
        LinhaImpressao.NovoCartao("Artigos", artigos.ToString()),
        LinhaImpressao.NovoCartao("Abaixo do mínimo", "1", alerta: true),
        LinhaImpressao.NovaFaixa("Ingredientes", $"{artigos} artigos · 5 162,50 MT"),
        LinhaImpressao.Tabela(["Código", "Artigo", "Quantidade", "Valor"], cabecalho: true, colunasTexto: 2),
        .. Enumerable.Range(0, artigos).Select(i => LinhaImpressao.Tabela([$"A{i:0000}", i == 1 ? "BATATA" : $"ARROZ {i}", "9,96 Kg", "4 980,00 MT"],
            alerta: i == 1, colunasTexto: 2)),
        LinhaImpressao.Tabela(["Subtotal Ingredientes", "", "", "5 162,50 MT"], subtotal: true, colunasTexto: 2),
        new("VALOR TOTAL DO STOCK", "5 162,50 MT", Negrito: true, Grande: true, Total: true),
        new("Linhas a vermelho: abaixo do mínimo.", SoA4: true),
        new("(!) abaixo do mínimo.", SoTalao: true),
    ]);

    [Fact]
    public void A4_desenha_faixa_quadros_alertas_subtotal_e_total()
    {
        var pagina = Assert.Single(Disposicao.Dispor(Stock(), 210, null, new Medidor()));
        var textos = pagina.Operacoes.OfType<OpTexto>().ToList();

        Assert.Contains(textos, t => t.Texto == "EXISTÊNCIAS EM STOCK" && t.Cor == CorTexto.Azul && t.Estilo == EstiloTexto.Grande);
        // Quadros de resumo: contorno, rótulo em maiúsculas a cinzento; o de alerta com fundo e valor a vermelho.
        Assert.Equal(3, pagina.Operacoes.OfType<OpCaixa>().Count()); // 2 quadros + o total
        Assert.Contains(textos, t => t.Texto == "ABAIXO DO MÍNIMO" && t.CorFinal == CorTexto.Cinzento);
        Assert.Contains(textos, t => t.Texto == "1" && t.Cor == CorTexto.Vermelho);
        // Faixa escura com o título em maiúsculas e o resumo a branco.
        Assert.Contains(pagina.Operacoes.OfType<OpFundo>(), f => f.Tipo == TipoFundo.Faixa);
        Assert.Contains(textos, t => t.Texto == "INGREDIENTES" && t.Cor == CorTexto.Branco);
        Assert.Contains(textos, t => t.Texto == "2 artigos · 5 162,50 MT" && t.Cor == CorTexto.Branco);
        // Colunas de texto à esquerda (código e artigo), valores à direita; alerta a vermelho com a marca.
        var codigo = textos.Single(t => t.Texto == "A0001");
        var batata = textos.Single(t => t.Texto == "BATATA");
        Assert.True(batata.X > codigo.X);
        Assert.Equal((Alinhamento.Esquerda, CorTexto.Vermelho), (batata.Alinhamento, batata.Cor));
        Assert.Contains(pagina.Operacoes.OfType<OpFundo>(), f => f.Tipo == TipoFundo.MarcaAlerta);
        Assert.Contains(textos, t => t.Texto == "Quantidade" && t.Alinhamento == Alinhamento.Direita && t.Cor == CorTexto.Azul);
        // Subtotal: o texto ocupa as duas colunas de texto, com traço forte por cima.
        var subtotal = textos.Single(t => t.Texto == "Subtotal Ingredientes");
        Assert.True(subtotal.Largura > batata.X - codigo.X);
        Assert.Contains(pagina.Operacoes.OfType<OpLinha>(), l => l.Forte);
        // Total num quadro forte com o valor a azul; legenda só em A4.
        Assert.Contains(pagina.Operacoes.OfType<OpCaixa>(), c => c.Forte);
        Assert.Contains(textos, t => t.Texto == "5 162,50 MT" && t.Cor == CorTexto.Azul);
        Assert.Contains(textos, t => t.Texto.StartsWith("Linhas a vermelho"));
        Assert.DoesNotContain(textos, t => t.Texto.StartsWith("(!)"));
    }

    [Fact]
    public void Talao_fica_compacto_a_preto_com_o_alerta_marcado()
    {
        var pagina = Assert.Single(Disposicao.Dispor(Stock(), 80, null, new Medidor()));
        var textos = pagina.Operacoes.OfType<OpTexto>().ToList();
        Assert.All(textos, t => Assert.Equal(CorTexto.Preto, t.CorFinal));
        Assert.Empty(pagina.Operacoes.OfType<OpCaixa>());
        Assert.DoesNotContain(pagina.Operacoes.OfType<OpFundo>(), f => f.Tipo == TipoFundo.Faixa);
        // Título e período centrados em duas linhas; colunas de texto juntas na descrição; alerta com «(!)».
        Assert.Contains(textos, t => t.Texto == "EXISTÊNCIAS EM STOCK" && t.Alinhamento == Alinhamento.Centro);
        Assert.Contains(textos, t => t.Texto == "Em 18/11/2026" && t.Alinhamento == Alinhamento.Centro);
        Assert.Contains(textos, t => t.Texto == "A0001 · BATATA (!)");
        Assert.Contains(textos, t => t.Texto == "(!) abaixo do mínimo.");
        Assert.DoesNotContain(textos, t => t.Texto.StartsWith("Linhas a vermelho"));
    }

    [Fact]
    public void Grelha_longa_repete_os_titulos_das_colunas_e_a_faixa_nao_fica_sozinha_no_fim_da_pagina()
    {
        var paginas = Disposicao.Dispor(Stock(120), 210, null, new Medidor());
        Assert.True(paginas.Count > 1);
        Assert.All(paginas.Skip(1), p => Assert.Contains(p.Operacoes.OfType<OpTexto>(), t => t.Texto == "Código" && t.Cor == CorTexto.Azul));
        // Faixa empurrada para a página seguinte quando não cabem ela, os títulos e a primeira linha.
        var curto = new DocumentoImpressao("Curto",
        [
            .. Enumerable.Range(0, 56).Select(i => new LinhaImpressao($"Linha {i}")),
            LinhaImpressao.NovaFaixa("Revenda"),
            LinhaImpressao.Tabela(["Artigo", "Valor"], cabecalho: true),
            LinhaImpressao.Tabela(["2M", "1,00"]),
        ]);
        var duas = Disposicao.Dispor(curto, 210, null, new Medidor());
        var faixa = duas.Select((p, i) => (i, p)).Single(x => x.p.Operacoes.OfType<OpTexto>().Any(t => t.Texto == "REVENDA")).i;
        var linha = duas.Select((p, i) => (i, p)).Single(x => x.p.Operacoes.OfType<OpTexto>().Any(t => t.Texto == "2M")).i;
        Assert.Equal(linha, faixa);
    }
}
