using System.Net;
using System.Net.Http.Headers;
using ClosedXML.Excel;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Importação de produtos, sempre com o modelo publicado em frontend/public/modelos (preenchido em memória).</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class ImportacaoProdutosTests(AplicacaoTeste app)
{
    private const string Analisar = "/api/produtos/importacao/analisar", Importar = "/api/produtos/importacao";

    private static string CaminhoModelo()
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            var ficheiro = Path.Combine(pasta.FullName, "frontend", "public", "modelos", "Modelo-Importacao-Produtos.xlsx");
            if (File.Exists(ficheiro))
                return ficheiro;
        }
        throw new FileNotFoundException("Modelo de importação não encontrado.");
    }

    /// <summary>Colunas do modelo: Código, Barras, Nome, Descrição, Categoria, Subcategoria, Tipo, Preço, Custo, Taxa, Cozinha, Disponível, Controla, Stock, Mínimo.</summary>
    private static MultipartFormDataContent Modelo(params object?[][] linhas)
    {
        using var livro = new XLWorkbook(CaminhoModelo());
        var folha = livro.Worksheet("Produtos");
        for (var l = 0; l < linhas.Length; l++)
            for (var c = 0; c < linhas[l].Length; c++)
                switch (linhas[l][c])
                {
                    case string s: folha.Cell(l + 2, c + 1).SetValue(s); break;
                    case double d: folha.Cell(l + 2, c + 1).SetValue(d); break;
                    case int n: folha.Cell(l + 2, c + 1).SetValue(n); break;
                }
        using var m = new MemoryStream();
        livro.SaveAs(m);
        return Ficheiro(m.ToArray());
    }

    private static MultipartFormDataContent Ficheiro(byte[] dados)
    {
        var conteudo = new ByteArrayContent(dados);
        conteudo.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return new MultipartFormDataContent { { conteudo, "ficheiro", "produtos.xlsx" } };
    }

    private static object?[] Linha(string codigo, string nome = "Produto", object? preco = null, string? barras = null, string categoria = "Importados", string tipo = "Bar") =>
        [codigo, barras, nome, null, categoria, null, tipo, preco ?? 10.5];

    private static string Cod() => Unico("I", 10).ToUpperInvariant();

    private async Task<ProdutoDto?> ProdutoAsync(HttpClient c, string codigo) =>
        (await c.Obter<Pagina<ProdutoDto>>($"/api/produtos?pesquisa={codigo}&tamanho=5")).Itens.FirstOrDefault(p => p.Codigo == codigo);

    [Fact]
    public async Task Modelo_preenchido_cria_os_produtos_categorias_e_stock_inicial()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var (prato, bebida) = (Cod(), Cod());
        var barras = "78" + Random.Shared.NextInt64(100_000_000_000, 999_999_999_999);
        var categoria = Unico("Cat", 10);
        var conteudo = () => Modelo(
            [prato.ToLowerInvariant(), null, "Frango importado", "Com batata", categoria, "Grelhados", "Restaurante", 450.5, 180, "IVA 16%", "Sim", "Sim", "Não"],
            // Texto com vírgula decimal (colado de outro lado) e código de barras escrito como número.
            [bebida, barras, "Água importada", null, categoria, null, "bar", "50,75", "20", "Isento", "não", null, null, 24, 6]);

        var analise = await (await admin.PostAsync(Analisar, conteudo()).Esperar(HttpStatusCode.OK)).Ler<AnaliseImportacao>();
        Assert.Equal((2, 0), (analise.Total, analise.ComErros));
        Assert.Equal([categoria, $"{categoria} › Grelhados"], analise.CategoriasNovas);
        Assert.Equal(2, analise.Linhas[0].Linha);
        Assert.Null(await ProdutoAsync(admin, prato)); // analisar não grava

        var resultado = await (await admin.PostAsync(Importar, conteudo()).Esperar(HttpStatusCode.OK)).Ler<ResultadoImportacao>();
        Assert.Equal(2, resultado.Importados);

        var p = (await ProdutoAsync(admin, prato))!;
        Assert.Equal(("Frango importado", "Com batata", TipoProduto.Restaurante, 450.5m, 180m), (p.Nome, p.Descricao, p.Tipo, p.PrecoVenda, p.Custo));
        Assert.Equal(("Grelhados", 16m, true, true, false), (p.CategoriaNome, p.TaxaImpostoPercentagem!.Value, p.EnviaCozinha, p.Disponivel, p.ControlaStock));
        Assert.Null(p.ArtigoId);

        var b = (await ProdutoAsync(admin, bebida))!;
        Assert.Equal((barras, 50.75m, TipoProduto.Bar, false, true, true), (b.CodigoBarras, b.PrecoVenda, b.Tipo, b.EnviaCozinha, b.Disponivel, b.ControlaStock));
        await using var db = app.CriarContexto();
        Assert.Equal(24m, await db.StockSaldos.Where(s => s.ArtigoId == b.ArtigoId).SumAsync(s => s.Quantidade));
        Assert.Equal(6m, await db.Artigos.Where(a => a.Id == b.ArtigoId).Select(a => a.StockMinimo).SingleAsync());

        // Importar o mesmo ficheiro de novo não duplica: os códigos já existem.
        var repetida = await (await admin.PostAsync(Analisar, conteudo()).Esperar(HttpStatusCode.OK)).Ler<AnaliseImportacao>();
        Assert.Equal(2, repetida.ComErros);
        Assert.Empty(repetida.CategoriasNovas);
        await admin.PostAsync(Importar, conteudo()).Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Erros_aparecem_linha_a_linha_e_nada_e_gravado()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var (boa, repetida) = (Cod(), Cod());
        var categoria = Unico("Cat", 10);
        var conteudo = () => Modelo(
            Linha(boa, categoria: categoria),
            Linha(repetida, categoria: categoria),
            Linha(repetida, categoria: categoria),
            Linha("com espaço", categoria: categoria),
            Linha(Cod(), nome: "", categoria: categoria),
            Linha(Cod(), tipo: "Cozinha", categoria: categoria),
            Linha(Cod(), preco: "1.234,50", categoria: categoria),
            Linha(Cod(), preco: -1, categoria: categoria),
            Linha(Cod(), categoria: ""),
            [Cod(), null, "Taxa", null, categoria, null, "Bar", 1, null, "IVA 99%"],
            [Cod(), null, "Sem stock", null, categoria, null, "Bar", 1, null, null, null, null, "Não", 5],
            [Cod(), null, "Sim/Não", null, categoria, null, "Bar", 1, null, null, "Talvez"]);

        var analise = await (await admin.PostAsync(Analisar, conteudo()).Esperar(HttpStatusCode.OK)).Ler<AnaliseImportacao>();
        Assert.Equal((12, 10), (analise.Total, analise.ComErros));
        string Erro(int linha) => Assert.Single(analise.Linhas.Single(l => l.Linha == linha).Erros);
        Assert.Empty(analise.Linhas.Single(l => l.Linha == 2).Erros);
        Assert.Contains("repetido no ficheiro (linha 3)", Erro(4));
        Assert.Contains("só pode ter", Erro(5));
        Assert.Contains("Indique o nome", Erro(6));
        Assert.Contains("Restaurante, Bar ou Outro", Erro(7));
        Assert.Contains("não é um número válido", Erro(8));
        Assert.Contains("entre 0 e", Erro(9));
        Assert.Contains("Indique a categoria", Erro(10));
        Assert.Contains("não existe", Erro(11));
        Assert.Contains("não controla stock", Erro(12));
        Assert.Contains("Sim ou Não", Erro(13));

        // Tudo ou nada: nem a linha boa nem a categoria entram.
        await admin.PostAsync(Importar, conteudo()).Esperar(HttpStatusCode.UnprocessableEntity);
        Assert.Null(await ProdutoAsync(admin, boa));
        Assert.DoesNotContain(await admin.Obter<List<CategoriaDto>>("/api/categorias"), c => c.Nome == categoria);
    }

    [Fact]
    public async Task Colunas_de_unidade_armazem_e_quantidade()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var armazem = await (await admin.Post("/api/stock/armazens", new Application.Stock.GuardarArmazem(Unico("Arm ", 20))).Esperar(HttpStatusCode.OK)).Ler<Application.Stock.ArmazemDto>();
        var codigo = Cod();
        object?[] Com(string cod, string unidade, string nomeArmazem, string pede) =>
            [cod, null, "Arroz", null, "Importados", null, "Outro", 90, null, null, "Não", null, null, 10, null, unidade, nomeArmazem, pede];

        var errada = await (await admin.PostAsync(Analisar, Modelo(Com(Cod(), "saca", "Não existe", "Talvez"))).Esperar(HttpStatusCode.OK)).Ler<AnaliseImportacao>();
        Assert.Equal(3, Assert.Single(errada.Linhas).Erros.Count);

        await admin.PostAsync(Importar, Modelo(Com(codigo, "kg", armazem.Nome, "Sim"))).Esperar(HttpStatusCode.OK);
        var p = (await ProdutoAsync(admin, codigo))!;
        Assert.Equal(("kg", armazem.Id, true), (p.Unidade, p.ArmazemId, p.PedeQuantidade));
        await using var db = app.CriarContexto();
        Assert.Equal(10m, await db.StockSaldos.Where(s => s.ArtigoId == p.ArtigoId && s.ArmazemId == armazem.Id).Select(s => s.Quantidade).SingleAsync());
    }

    [Fact]
    public async Task GERAR_cria_codigos_de_barras_EAN13_e_coluna_da_impressora_dos_pedidos()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        const string UrlImpressoras = "/api/configuracoes/impressoras";
        var cozinha = await (await admin.Post(UrlImpressoras, new Application.Impressao.GuardarImpressora(Unico("Coz ", 20), FuncaoImpressora.Cozinha, ImpressorasTeste.Cozinha, 80, null, null))
            .Esperar(HttpStatusCode.OK)).Ler<Application.Impressao.ImpressoraDto>();
        try
        {
            object?[] Com(string cod, string barras, string impressora) =>
                [cod, barras, "Sopa", null, "Importados", null, "Restaurante", 80, null, null, null, null, "Não", null, null, null, null, null, impressora];

            var errada = await (await admin.PostAsync(Analisar, Modelo(Com(Cod(), "gerar", "Não existe"))).Esperar(HttpStatusCode.OK)).Ler<AnaliseImportacao>();
            Assert.Contains("A impressora «Não existe» não existe, está inactiva ou não é de Bar/Cozinha.", Assert.Single(errada.Linhas).Erros);

            // Várias linhas com GERAR não contam como repetidas; cada uma recebe um EAN-13 interno diferente.
            var (c1, c2, c3) = (Cod(), Cod(), Cod());
            var r = await (await admin.PostAsync(Importar, Modelo(Com(c1, "GERAR", cozinha.Nome), Com(c2, "gerar", ""), Com(c3, "", ""))).Esperar(HttpStatusCode.OK)).Ler<ResultadoImportacao>();
            Assert.Equal(3, r.Importados);
            var p1 = (await ProdutoAsync(admin, c1))!;
            var p2 = (await ProdutoAsync(admin, c2))!;
            foreach (var g in new[] { p1.CodigoBarras!, p2.CodigoBarras! })
            {
                Assert.Matches("^20[0-9]{11}$", g);
                Assert.Equal(g, ServicoCatalogo.Ean13(g[..12]));
            }
            Assert.NotEqual(p1.CodigoBarras, p2.CodigoBarras);
            Assert.Null((await ProdutoAsync(admin, c3))!.CodigoBarras);
            Assert.Equal(cozinha.Id, p1.ImpressoraId);
            Assert.Null(p2.ImpressoraId);
        }
        finally
        {
            await admin.Post($"{UrlImpressoras}/{cozinha.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Exige_permissoes_e_recusa_ficheiros_invalidos()
    {
        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        Assert.Equal(HttpStatusCode.Forbidden, (await garcom.PostAsync(Importar, Modelo(Linha(Cod())))).StatusCode);

        // Quem gere produtos mas não ajusta stock não lança stock inicial.
        var gestor = await app.ClienteComPermissoesAsync(Permissoes.ProdutosGerir);
        var analise = await (await gestor.PostAsync(Analisar, Modelo([Cod(), null, "Com stock", null, "Importados", null, "Bar", 1, null, null, null, null, null, 3]))
            .Esperar(HttpStatusCode.OK)).Ler<AnaliseImportacao>();
        Assert.Contains("permissão de ajustes de stock", Assert.Single(Assert.Single(analise.Linhas).Erros));

        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        await admin.PostAsync(Analisar, Ficheiro("isto não é excel"u8.ToArray())).Esperar(HttpStatusCode.UnprocessableEntity);

        using var semTitulos = new XLWorkbook();
        semTitulos.AddWorksheet("Produtos").Cell(1, 1).SetValue("Outra coisa");
        using var m = new MemoryStream();
        semTitulos.SaveAs(m);
        await admin.PostAsync(Analisar, Ficheiro(m.ToArray())).Esperar(HttpStatusCode.UnprocessableEntity);

        var vazio = await (await admin.PostAsync(Analisar, Modelo()).Esperar(HttpStatusCode.OK)).Ler<AnaliseImportacao>();
        Assert.Equal(0, vazio.Total);
        await admin.PostAsync(Importar, Modelo()).Esperar(HttpStatusCode.UnprocessableEntity);
    }
}
