using System.ComponentModel.DataAnnotations;
using System.Globalization;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Impressao;

/// <summary>Pré-visualização de um documento em imagem (só em Windows; null noutros sistemas).</summary>
public interface IPrevisaoImpressao
{
    byte[]? Png(DocumentoImpressao documento);
}

public sealed record ItemEtiquetas(
    [Range(1, int.MaxValue, ErrorMessage = "Produto inválido.")] int ProdutoId,
    [Range(1, 500, ErrorMessage = "A quantidade de etiquetas é de 1 a 500.")] int Quantidade);

public sealed record ImprimirEtiquetas([MinLength(1, ErrorMessage = "Indique pelo menos um produto.")] IReadOnlyList<ItemEtiquetas> Itens);

/// <summary>Tamanho da etiqueta (mm) e dados do produto que aparecem nela, pela ordem em que saem.</summary>
public sealed record ConfiguracaoEtiqueta(decimal LarguraMm, decimal AlturaMm, IReadOnlyList<string> Campos);

/// <summary>
/// Etiquetas de produto (Configurações → Etiquetas): tamanho, campos e impressora (uma impressora com a função «Etiquetas»,
/// no servidor ou num posto). Cada etiqueta é uma página do rolo, com o mesmo desenho na impressora e na pré-visualização.
/// </summary>
public class ServicoEtiquetas(IAppDbContext db, ConfiguracoesSistema config, IFilaImpressao fila, IPrevisaoImpressao previsao)
{
    public const string Nome = "nome", Categoria = "categoria", Codigo = "codigo", Preco = "preco", Unidade = "unidade", CodigoBarras = "codigo-barras";
    public static readonly IReadOnlyList<string> CamposPossiveis = [Nome, Categoria, Codigo, Preco, Unidade, CodigoBarras];
    public const decimal LarguraMinima = 20, LarguraMaxima = 150, AlturaMinima = 10, AlturaMaxima = 150;
    public const int MaximoPorImpressao = 500;

    private sealed record DadosProduto(string Nome, string Categoria, string Codigo, decimal Preco, string Unidade, string? CodigoBarras);

    public static string? ValidarCampos(string valor) =>
        valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is var campos
        && campos.All(CamposPossiveis.Contains) && campos.Distinct().Count() == campos.Length
            ? null : $"Campos possíveis: {string.Join(", ", CamposPossiveis)}.";

    public async Task<ConfiguracaoEtiqueta> ConfiguracaoAsync()
    {
        var largura = await config.DecimalAsync(ChavesConfiguracao.EtiquetaLargura, 50);
        var altura = await config.DecimalAsync(ChavesConfiguracao.EtiquetaAltura, 30);
        var campos = (await config.TextoAsync(ChavesConfiguracao.EtiquetaCampos, "nome,preco,codigo-barras"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(CamposPossiveis.Contains).Distinct().ToList();
        return new ConfiguracaoEtiqueta(Math.Clamp(largura, LarguraMinima, LarguraMaxima), Math.Clamp(altura, AlturaMinima, AlturaMaxima), campos);
    }

    /// <summary>Envia as etiquetas para a impressora de etiquetas (em segundo plano). Devolve o nome da impressora.</summary>
    public async Task<string> ImprimirAsync(ImprimirEtiquetas dados, CancellationToken ct)
    {
        if (dados.Itens.Sum(i => i.Quantidade) > MaximoPorImpressao)
            throw new RegraNegocioException($"No máximo {MaximoPorImpressao} etiquetas de cada vez.");
        var ids = dados.Itens.Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await ProdutosAsync(ids, ct);
        if (produtos.Count != ids.Count)
            throw new NaoEncontradoException("Produto não encontrado.");
        var cfg = await ConfiguracaoAsync();
        var moeda = await config.TextoAsync(ChavesConfiguracao.SimboloMoeda, "MT");
        return await ImprimirAsync([.. dados.Itens.SelectMany(i => Enumerable.Repeat(Compor(produtos[i.ProdutoId], cfg, moeda), i.Quantidade))], ct);
    }

    /// <summary>Etiquetas já compostas (ex.: Cartões Evento), na impressora de etiquetas, com o tamanho configurado.</summary>
    public async Task<string> ImprimirAsync(IReadOnlyList<EtiquetaImpressao> etiquetas, CancellationToken ct)
    {
        if (etiquetas.Count > MaximoPorImpressao)
            throw new RegraNegocioException($"No máximo {MaximoPorImpressao} etiquetas de cada vez.");
        var impressora = await db.Impressoras.AsNoTracking().Where(i => i.Activo && i.Funcao == FuncaoImpressora.Etiquetas).OrderBy(i => i.Id)
            .Select(i => new { i.Id, i.Nome }).FirstOrDefaultAsync(ct)
            ?? throw new RegraNegocioException("Não há impressora de etiquetas. Configure-a em Configurações → Etiquetas.");
        fila.Enfileirar(new ImprimirDocumento(impressora.Id, Documento(etiquetas, await ConfiguracaoAsync())));
        return impressora.Nome;
    }

    /// <summary>Pré-visualização de uma etiqueta já composta, com o tamanho configurado.</summary>
    public async Task<byte[]?> PrevisaoAsync(EtiquetaImpressao etiqueta) => previsao.Png(Documento([etiqueta], await ConfiguracaoAsync()));

    /// <summary>
    /// Etiqueta em PNG, com o tamanho e os campos indicados (ou os guardados): o produto escolhido ou um de exemplo.
    /// Null se o servidor não puder desenhar (não Windows).
    /// </summary>
    public async Task<byte[]?> PrevisaoAsync(int? produtoId, decimal? largura, decimal? altura, string? campos, CancellationToken ct)
    {
        var guardada = await ConfiguracaoAsync();
        if (campos is not null && ValidarCampos(campos) is { } erro)
            throw new RegraNegocioException(erro);
        var cfg = new ConfiguracaoEtiqueta(
            Math.Clamp(largura ?? guardada.LarguraMm, LarguraMinima, LarguraMaxima),
            Math.Clamp(altura ?? guardada.AlturaMm, AlturaMinima, AlturaMaxima),
            campos is null ? guardada.Campos : [.. campos.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]);
        var produto = produtoId is int id
            ? (await ProdutosAsync([id], ct)).GetValueOrDefault(id) ?? throw new NaoEncontradoException("Produto não encontrado.")
            : new DadosProduto("Água mineral 500 ml", "Bebidas", "P0001", 50m, "un", "7891234567890");
        var moeda = await config.TextoAsync(ChavesConfiguracao.SimboloMoeda, "MT");
        return previsao.Png(Documento([Compor(produto, cfg, moeda)], cfg));
    }

    private async Task<Dictionary<int, DadosProduto>> ProdutosAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
        await db.Produtos.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                Dados = new DadosProduto(p.Nome, p.Categoria.Nome, p.Codigo, p.PrecoVenda,
                    p.UnidadeMedida != null ? p.UnidadeMedida.Codigo : "un", p.CodigoBarras),
            })
            .ToDictionaryAsync(x => x.Id, x => x.Dados, ct);

    private static DocumentoImpressao Documento(IReadOnlyList<EtiquetaImpressao> etiquetas, ConfiguracaoEtiqueta cfg) =>
        new("Etiquetas", [], null, etiquetas, new FormatoEtiqueta((double)cfg.LarguraMm, (double)cfg.AlturaMm));

    /// <summary>Uma etiqueta com os campos configurados. Sem código de barras próprio, usa o código interno (o POS lê os dois).</summary>
    private static EtiquetaImpressao Compor(DadosProduto p, ConfiguracaoEtiqueta cfg, string moeda)
    {
        var linhas = new List<LinhaImpressao>();
        string? barras = null;
        var comUnidade = cfg.Campos.Contains(Unidade);
        foreach (var campo in cfg.Campos)
        {
            switch (campo)
            {
                case Nome: linhas.Add(new(p.Nome, Alinhamento: Alinhamento.Centro, Negrito: true)); break;
                case Categoria: linhas.Add(new(p.Categoria, Alinhamento: Alinhamento.Centro)); break;
                case Codigo: linhas.Add(new($"Cód. {p.Codigo}", Alinhamento: Alinhamento.Centro)); break;
                case Preco: linhas.Add(new(ComposicaoImpressao.Valor(p.Preco, moeda) + (comUnidade ? $" / {p.Unidade}" : ""), Alinhamento: Alinhamento.Centro, Grande: true)); break;
                case Unidade when !cfg.Campos.Contains(Preco): linhas.Add(new($"Unidade: {p.Unidade}", Alinhamento: Alinhamento.Centro)); break;
                case CodigoBarras: barras = p.CodigoBarras ?? p.Codigo; break;
            }
        }
        return new EtiquetaImpressao(linhas, barras);
    }

    internal static decimal Mm(string valor) =>
        decimal.TryParse(valor, ConfiguracoesSistema.EstiloDecimal, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
