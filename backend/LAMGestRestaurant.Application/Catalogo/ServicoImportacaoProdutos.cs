using System.Globalization;
using System.Text.RegularExpressions;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Catalogo;

/// <summary>Uma linha de dados da folha (número da linha no Excel e valor de cada coluna, como texto; números com ponto decimal).</summary>
public sealed record LinhaFolha(int Numero, IReadOnlyDictionary<string, string> Valores);

/// <summary>Leitura de uma folha de um ficheiro Excel (.xlsx).</summary>
public interface ILeitorFolhaExcel
{
    /// <summary>Linhas com dados. Ficheiro inválido, folha ou títulos em falta, ou linhas a mais: <see cref="RegraNegocioException"/>.</summary>
    IReadOnlyList<LinhaFolha> Ler(Stream ficheiro, string folha, IReadOnlyList<string> titulos, IReadOnlyList<string> titulosOpcionais, int maximoLinhas);
}

public sealed record LinhaImportacao(int Linha, string Codigo, string Nome, string Categoria, string? Subcategoria, decimal? PrecoVenda, decimal? StockInicial, IReadOnlyList<string> Erros);

public sealed record AnaliseImportacao(int Total, int ComErros, IReadOnlyList<string> CategoriasNovas, IReadOnlyList<LinhaImportacao> Linhas);

public sealed record ResultadoImportacao(int Importados, IReadOnlyList<string> CategoriasCriadas);

/// <summary>
/// Importação de produtos a partir do modelo Excel. Valida o ficheiro inteiro e mostra os erros linha a linha; só grava se não
/// houver nenhum, tudo numa transacção (ou entram todos os produtos, ou nenhum). Cada produto é criado pelo mesmo caminho do
/// cadastro manual (artigo de stock, ficha técnica, stock inicial e auditoria). As categorias em falta são criadas.
/// </summary>
public sealed partial class ServicoImportacaoProdutos(IAppDbContext db, ServicoCatalogo catalogo, ILeitorFolhaExcel leitor, IUtilizadorActual actual, IAuditoria auditoria)
{
    public const long BytesMaximos = 10 * 1024 * 1024;
    public const int LinhasMaximas = 2000;
    private const string Folha = "Produtos";
    private const decimal MaximoValor = 999_999_999m, MaximoQuantidade = 99_999_999m;

    private const string TCodigo = "Código", TBarras = "Código de barras", TNome = "Nome", TDescricao = "Descrição", TCategoria = "Categoria",
        TSubcategoria = "Subcategoria", TTipo = "Tipo", TPreco = "Preço de venda (MT)", TCusto = "Custo (MT)", TTaxa = "Taxa de imposto",
        TCozinha = "Vai à cozinha", TDisponivel = "Disponível", TControla = "Controla stock", TStock = "Stock inicial", TMinimo = "Stock mínimo",
        TUnidade = "Unidade", TArmazem = "Armazém", TPedeQuantidade = "Pede quantidade";

    private static readonly string[] Titulos = [TCodigo, TBarras, TNome, TDescricao, TCategoria, TSubcategoria, TTipo, TPreco, TCusto, TTaxa, TCozinha, TDisponivel, TControla, TStock, TMinimo];
    private static readonly string[] Opcionais = [TUnidade, TArmazem, TPedeQuantidade];

    [GeneratedRegex("^[A-Za-z0-9._-]+$")] private static partial Regex RegexCodigo();
    [GeneratedRegex("^[0-9A-Za-z-]*$")] private static partial Regex RegexBarras();

    private sealed record Valida(GuardarProduto Dados, string Categoria, string? Subcategoria);
    private sealed record Analisada(LinhaImportacao Vista, Valida? Dados);
    private sealed record CategoriaExistente(int Id, string Nome, int? PaiId, bool Activo);

    public async Task<AnaliseImportacao> AnalisarAsync(Stream ficheiro, CancellationToken ct)
    {
        var (linhas, novas) = await ValidarAsync(ficheiro, ct);
        return new AnaliseImportacao(linhas.Count, linhas.Count(l => l.Vista.Erros.Count > 0), novas, [.. linhas.Select(l => l.Vista)]);
    }

    public async Task<ResultadoImportacao> ImportarAsync(Stream ficheiro, CancellationToken ct)
    {
        var (linhas, novas) = await ValidarAsync(ficheiro, ct);
        if (linhas.Count == 0)
            throw new RegraNegocioException("O ficheiro não tem produtos: preencha a folha «Produtos» a partir da linha 2.");
        if (linhas.Count(l => l.Dados is null) is var comErros and > 0)
            throw new RegraNegocioException($"O ficheiro tem {comErros} linha(s) com erros e nada foi gravado. Corrija-as e carregue o ficheiro de novo.");

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        var categorias = await db.Categorias.Select(c => new CategoriaExistente(c.Id, c.Nome, c.CategoriaPaiId, c.Activo)).ToListAsync(ct);
        var principais = categorias.Where(c => c.PaiId is null).GroupBy(c => c.Nome, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var filhas = categorias.Where(c => c.PaiId is not null).GroupBy(c => (c.PaiId!.Value, c.Nome.ToUpperInvariant())).ToDictionary(g => g.Key, g => g.First().Id);

        async Task<int> CriarCategoria(string nome, int? pai)
        {
            var nova = new Categoria { Nome = nome, CategoriaPaiId = pai, Activo = true };
            db.Categorias.Add(nova);
            await db.SaveChangesAsync(ct);
            auditoria.Registar("categoria.criar", "Categoria", nova.Id, new { nova.Nome, Origem = "importação" });
            return nova.Id;
        }

        var produtos = new List<string>();
        foreach (var l in linhas.Select(x => x.Dados!))
        {
            if (!principais.TryGetValue(l.Categoria, out var categoriaId))
                principais[l.Categoria] = categoriaId = await CriarCategoria(l.Categoria, null);
            if (l.Subcategoria is { } sub)
            {
                var chave = (categoriaId, sub.ToUpperInvariant());
                if (!filhas.TryGetValue(chave, out var filhaId))
                    filhas[chave] = filhaId = await CriarCategoria(sub, categoriaId);
                categoriaId = filhaId;
            }
            var produto = await catalogo.CriarProdutoEmTransacaoAsync(l.Dados with { CategoriaId = categoriaId });
            produtos.Add(produto.Codigo);
            db.ChangeTracker.Clear(); // já gravado: sem isto cada gravação fica mais lenta à medida que o ficheiro avança
        }
        auditoria.Registar("produto.importar", "Produto", null, new { Quantidade = produtos.Count, CategoriasNovas = novas, Codigos = produtos.Take(50) });
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return new ResultadoImportacao(produtos.Count, novas);
    }

    private async Task<(IReadOnlyList<Analisada> Linhas, IReadOnlyList<string> CategoriasNovas)> ValidarAsync(Stream ficheiro, CancellationToken ct)
    {
        var folha = leitor.Ler(ficheiro, Folha, Titulos, Opcionais, LinhasMaximas);

        var codigos = (await db.Produtos.AsNoTracking().Select(p => p.Codigo).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var barras = (await db.Produtos.AsNoTracking().Where(p => p.CodigoBarras != null).Select(p => p.CodigoBarras!).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var taxas = await db.TaxasImposto.AsNoTracking().Select(t => new { t.Id, t.Nome, t.Activo }).ToListAsync(ct);
        var categorias = await db.Categorias.AsNoTracking().Select(c => new CategoriaExistente(c.Id, c.Nome, c.CategoriaPaiId, c.Activo)).ToListAsync(ct);
        var unidades = await db.UnidadesMedida.AsNoTracking().Where(u => u.Activo).Select(u => new { u.Id, u.Codigo, u.Nome }).ToListAsync(ct);
        var armazens = await db.Armazens.AsNoTracking().Where(a => a.Activo).Select(a => new { a.Id, a.Nome }).ToListAsync(ct);
        var podeLancarStock = actual.TemPermissao(Permissoes.StockAjustar);

        var codigosFicheiro = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var barrasFicheiro = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var novas = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var resultado = new List<Analisada>(folha.Count);

        foreach (var linha in folha)
        {
            var erros = new List<string>();
            string V(string titulo) => linha.Valores.GetValueOrDefault(titulo, "").Trim();

            var codigo = V(TCodigo).ToUpperInvariant();
            if (codigo.Length == 0) erros.Add("Indique o código.");
            else if (codigo.Length > 30) erros.Add("O código não pode exceder 30 caracteres.");
            else if (!RegexCodigo().IsMatch(codigo)) erros.Add("O código só pode ter letras, algarismos e . _ -");
            else if (codigos.Contains(codigo)) erros.Add($"Já existe um produto com o código {codigo}.");
            else if (!codigosFicheiro.TryAdd(codigo, linha.Numero)) erros.Add($"Código repetido no ficheiro (linha {codigosFicheiro[codigo]}).");

            var codigoBarras = V(TBarras);
            if (codigoBarras.Length > 50) erros.Add("O código de barras não pode exceder 50 caracteres.");
            else if (!RegexBarras().IsMatch(codigoBarras)) erros.Add("Código de barras inválido.");
            else if (codigoBarras.Length > 0)
            {
                if (barras.Contains(codigoBarras)) erros.Add($"Já existe um produto com o código de barras {codigoBarras}.");
                else if (!barrasFicheiro.TryAdd(codigoBarras, linha.Numero)) erros.Add($"Código de barras repetido no ficheiro (linha {barrasFicheiro[codigoBarras]}).");
            }

            var nome = V(TNome);
            if (nome.Length == 0) erros.Add("Indique o nome.");
            else if (nome.Length > 150) erros.Add("O nome não pode exceder 150 caracteres.");
            var descricao = V(TDescricao);
            if (descricao.Length > 1000) erros.Add("A descrição não pode exceder 1000 caracteres.");

            var categoria = V(TCategoria);
            var subcategoria = V(TSubcategoria);
            var principal = categorias.FirstOrDefault(c => c.PaiId is null && string.Equals(c.Nome, categoria, StringComparison.OrdinalIgnoreCase));
            if (categoria.Length == 0) erros.Add("Indique a categoria.");
            else if (categoria.Length > 100) erros.Add("A categoria não pode exceder 100 caracteres.");
            else if (principal is { Activo: false }) erros.Add($"A categoria {principal.Nome} está inactiva.");
            if (subcategoria.Length > 100) erros.Add("A subcategoria não pode exceder 100 caracteres.");
            var filha = principal is null ? null : categorias.FirstOrDefault(c => c.PaiId == principal.Id && string.Equals(c.Nome, subcategoria, StringComparison.OrdinalIgnoreCase));
            if (filha is { Activo: false }) erros.Add($"A subcategoria {filha.Nome} está inactiva.");

            TipoProduto? tipo = V(TTipo).ToUpperInvariant() switch
            {
                "RESTAURANTE" => TipoProduto.Restaurante,
                "BAR" => TipoProduto.Bar,
                "OUTRO" => TipoProduto.Outro,
                _ => null,
            };
            if (tipo is null) erros.Add("O tipo tem de ser Restaurante, Bar ou Outro.");

            var preco = Numero(V(TPreco), "O preço de venda", MaximoValor, erros, obrigatorio: true);
            var custo = Numero(V(TCusto), "O custo", MaximoValor, erros);
            var stock = Numero(V(TStock), "O stock inicial", MaximoQuantidade, erros);
            var minimo = Numero(V(TMinimo), "O stock mínimo", MaximoQuantidade, erros);

            int? taxaId = null;
            var nomeTaxa = V(TTaxa);
            if (nomeTaxa.Length > 0)
            {
                var taxa = taxas.FirstOrDefault(t => string.Equals(t.Nome, nomeTaxa, StringComparison.OrdinalIgnoreCase));
                if (taxa is null) erros.Add($"A taxa de imposto «{nomeTaxa}» não existe.");
                else if (!taxa.Activo) erros.Add($"A taxa de imposto {taxa.Nome} está inactiva.");
                else taxaId = taxa.Id;
            }

            var cozinha = SimNao(V(TCozinha), TCozinha, erros);
            var disponivel = SimNao(V(TDisponivel), TDisponivel, erros);
            var controla = SimNao(V(TControla), TControla, erros);
            var pedeQuantidade = V(TPedeQuantidade).Length > 0 && SimNao(V(TPedeQuantidade), TPedeQuantidade, erros);

            int? unidadeId = null;
            var nomeUnidade = V(TUnidade);
            if (nomeUnidade.Length > 0)
            {
                unidadeId = unidades.FirstOrDefault(u => string.Equals(u.Codigo, nomeUnidade, StringComparison.OrdinalIgnoreCase) || string.Equals(u.Nome, nomeUnidade, StringComparison.OrdinalIgnoreCase))?.Id;
                if (unidadeId is null) erros.Add($"A unidade «{nomeUnidade}» não existe ou está inactiva.");
            }
            int? armazemId = null;
            var nomeArmazem = V(TArmazem);
            if (nomeArmazem.Length > 0)
            {
                armazemId = armazens.FirstOrDefault(a => string.Equals(a.Nome, nomeArmazem, StringComparison.OrdinalIgnoreCase))?.Id;
                if (armazemId is null) erros.Add($"O armazém «{nomeArmazem}» não existe ou está inactivo.");
            }

            var temStock = V(TStock).Length > 0 || V(TMinimo).Length > 0;
            if (temStock && !controla) erros.Add("Um produto que não controla stock não leva stock inicial nem mínimo.");
            if (stock is > 0 && !podeLancarStock) erros.Add("Lançar o stock inicial exige a permissão de ajustes de stock.");

            Valida? dados = null;
            if (erros.Count == 0)
            {
                if (principal is null) novas.Add(categoria);
                if (subcategoria.Length > 0 && filha is null) novas.Add($"{categoria} › {subcategoria}");
                dados = new Valida(
                    new GuardarProduto(codigo, codigoBarras.Length > 0 ? codigoBarras : null, nome, descricao.Length > 0 ? descricao : null, 0, tipo!.Value, preco!.Value, custo ?? 0,
                        taxaId, null, cozinha, disponivel, null, null,
                        temStock ? new StockInicialProduto(decimal.Round(stock ?? 0, 3, MidpointRounding.AwayFromZero), custo ?? 0, decimal.Round(minimo ?? 0, 3, MidpointRounding.AwayFromZero), null) : null,
                        controla, unidadeId, armazemId, pedeQuantidade),
                    categoria, subcategoria.Length > 0 ? subcategoria : null);
            }
            resultado.Add(new Analisada(new LinhaImportacao(linha.Numero, codigo, nome, categoria, subcategoria.Length > 0 ? subcategoria : null, preco, stock, erros), dados));
        }
        return (resultado, [.. novas]);
    }

    /// <summary>Número da folha: ponto ou vírgula decimal, nunca separadores de milhares (nem a cultura do servidor).</summary>
    private static decimal? Numero(string texto, string nome, decimal maximo, List<string> erros, bool obrigatorio = false)
    {
        if (texto.Length == 0)
        {
            if (obrigatorio) erros.Add($"Indique {nome.ToLowerInvariant()}.");
            return null;
        }
        if (!decimal.TryParse(texto.Replace(',', '.'), ConfiguracoesSistema.EstiloDecimal, CultureInfo.InvariantCulture, out var valor))
        {
            erros.Add($"{nome} não é um número válido: {texto}.");
            return null;
        }
        if (valor < 0 || valor > maximo)
        {
            erros.Add($"{nome} tem de estar entre 0 e {maximo.ToString("0", CultureInfo.InvariantCulture)}.");
            return null;
        }
        return valor;
    }

    /// <summary>Sim/Não; vazio = Sim.</summary>
    private static bool SimNao(string texto, string coluna, List<string> erros)
    {
        switch (texto.ToUpperInvariant())
        {
            case "" or "SIM": return true;
            case "NÃO" or "NAO": return false;
            default:
                erros.Add($"{coluna} tem de ser Sim ou Não.");
                return true;
        }
    }
}
