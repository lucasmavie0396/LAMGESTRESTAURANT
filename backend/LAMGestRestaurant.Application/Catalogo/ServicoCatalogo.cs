using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Catalogo;

/// <summary>
/// Categorias e produtos. Nada é eliminado: produtos e categorias usados em documentos só se desactivam.
/// Alterar o preço de um produto existente exige a permissão produtos.alterar-preco e fica auditado.
/// </summary>
public class ServicoCatalogo(IAppDbContext db, IUtilizadorActual actual, IAuditoria auditoria, MotorStock motor, TimeProvider relogio)
{
    public const string OrigemStockInicial = "Produto";

    // ---------- Categorias ----------

    public async Task<IReadOnlyList<CategoriaDto>> ListarCategoriasAsync(bool incluirInactivas, CancellationToken ct)
    {
        var consulta = db.Categorias.AsNoTracking();
        if (!incluirInactivas)
            consulta = consulta.Where(c => c.Activo);
        return await consulta
            .OrderBy(c => c.Ordem).ThenBy(c => c.Nome)
            .Select(c => new CategoriaDto(
                c.Id, c.Nome, c.Descricao, c.CategoriaPaiId, c.CategoriaPai != null ? c.CategoriaPai.Nome : null,
                c.EstacaoPreparacaoId, c.EstacaoPreparacao != null ? c.EstacaoPreparacao.Nome : null,
                c.Cor, c.Ordem, c.Activo,
                db.Produtos.Count(p => p.CategoriaId == c.Id && p.Activo),
                c.ImpressoraId, c.Impressora != null ? c.Impressora.Nome : null))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ImpressoraTalao>> ListarImpressorasTalaoAsync(CancellationToken ct) =>
        await db.Impressoras.AsNoTracking().Where(i => i.Funcao != FuncaoImpressora.Caixa)
            .OrderBy(i => i.Funcao).ThenBy(i => i.Nome)
            .Select(i => new ImpressoraTalao(i.Id, i.Nome, i.Funcao, i.Activo)).ToListAsync(ct);

    /// <summary>Só impressoras de talões (Bar/Cozinha) activas; as de caixa imprimem recibos.</summary>
    private async Task ValidarImpressoraTalaoAsync(int? impressoraId)
    {
        if (impressoraId is int id && !await db.Impressoras.AnyAsync(i => i.Id == id && i.Activo && i.Funcao != FuncaoImpressora.Caixa))
            throw new RegraNegocioException("Impressora inexistente, inactiva ou de caixa (escolha uma impressora de Bar ou Cozinha).");
    }

    public async Task<CategoriaDto> CriarCategoriaAsync(GuardarCategoria dados)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var categoria = new Categoria();
        await AplicarCategoria(categoria, dados);
        db.Categorias.Add(categoria);
        await db.SaveChangesAsync();
        auditoria.Registar("categoria.criar", "Categoria", categoria.Id, new { categoria.Nome });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterCategoria(categoria.Id);
    }

    public async Task<CategoriaDto> EditarCategoriaAsync(int id, GuardarCategoria dados)
    {
        var categoria = await db.Categorias.FindAsync(id) ?? throw new NaoEncontradoException("Categoria não encontrada.");
        await AplicarCategoria(categoria, dados);
        auditoria.Registar("categoria.editar", "Categoria", id, new { categoria.Nome });
        await db.SaveChangesAsync();
        return await ObterCategoria(id);
    }

    public async Task DefinirCategoriaActivaAsync(int id, bool activa)
    {
        var categoria = await db.Categorias.FindAsync(id) ?? throw new NaoEncontradoException("Categoria não encontrada.");
        if (!activa)
        {
            if (await db.Produtos.AnyAsync(p => p.CategoriaId == id && p.Activo))
                throw new RegraNegocioException("A categoria tem produtos activos. Mova-os ou desactive-os primeiro.");
            if (await db.Categorias.AnyAsync(c => c.CategoriaPaiId == id && c.Activo))
                throw new RegraNegocioException("A categoria tem subcategorias activas. Desactive-as primeiro.");
        }
        else if (categoria.CategoriaPaiId is int pai && !await db.Categorias.AnyAsync(c => c.Id == pai && c.Activo))
        {
            throw new RegraNegocioException("A categoria principal está inactiva. Active-a primeiro.");
        }
        categoria.Activo = activa;
        auditoria.Registar(activa ? "categoria.activar" : "categoria.desactivar", "Categoria", id, new { categoria.Nome });
        await db.SaveChangesAsync();
    }

    private async Task AplicarCategoria(Categoria categoria, GuardarCategoria dados)
    {
        var nome = dados.Nome.Trim();
        if (await db.Categorias.AnyAsync(c => c.Id != categoria.Id && c.CategoriaPaiId == dados.CategoriaPaiId && c.Nome == nome))
            throw new ConflitoException("Já existe uma categoria com este nome no mesmo nível.");

        if (dados.CategoriaPaiId is int paiId)
        {
            if (paiId == categoria.Id)
                throw new RegraNegocioException("Uma categoria não pode ser subcategoria de si própria.");
            // Impede ciclos: o pai escolhido não pode descender desta categoria.
            var cursor = await db.Categorias.AsNoTracking().FirstOrDefaultAsync(c => c.Id == paiId)
                         ?? throw new RegraNegocioException("Categoria principal inexistente.");
            if (!cursor.Activo)
                throw new RegraNegocioException("A categoria principal está inactiva.");
            var visitados = new HashSet<int>();
            while (cursor.CategoriaPaiId is int seguinte && visitados.Add(cursor.Id))
            {
                if (seguinte == categoria.Id && categoria.Id != 0)
                    throw new RegraNegocioException("Esta escolha criaria um ciclo de categorias.");
                cursor = await db.Categorias.AsNoTracking().FirstAsync(c => c.Id == seguinte);
            }
        }
        if (dados.EstacaoPreparacaoId is int estacao && !await db.EstacoesPreparacao.AnyAsync(e => e.Id == estacao && e.Activo))
            throw new RegraNegocioException("Estação de preparação inexistente ou inactiva.");
        if (dados.ImpressoraId != categoria.ImpressoraId)
            await ValidarImpressoraTalaoAsync(dados.ImpressoraId);

        categoria.ImpressoraId = dados.ImpressoraId;
        categoria.Nome = nome;
        categoria.Descricao = string.IsNullOrWhiteSpace(dados.Descricao) ? null : dados.Descricao.Trim();
        categoria.CategoriaPaiId = dados.CategoriaPaiId;
        categoria.EstacaoPreparacaoId = dados.EstacaoPreparacaoId;
        categoria.Cor = string.IsNullOrWhiteSpace(dados.Cor) ? null : dados.Cor.ToUpperInvariant();
        categoria.Ordem = dados.Ordem;
    }

    private async Task<CategoriaDto> ObterCategoria(int id) =>
        (await ListarCategoriasAsync(true, default)).Single(c => c.Id == id);

    // ---------- Produtos ----------

    public async Task<Pagina<ProdutoDto>> ListarProdutosAsync(FiltroProdutos f, CancellationToken ct)
    {
        var (pagina, tamanho) = Pagina<ProdutoDto>.Normalizar(f.Pagina, f.Tamanho);
        var consulta = db.Produtos.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(f.Pesquisa))
        {
            var p = f.Pesquisa.Trim();
            consulta = consulta.Where(x => x.Nome.Contains(p) || x.Codigo.Contains(p) || x.CodigoBarras == p || x.Categoria.Nome.Contains(p));
        }
        if (f.CategoriaId is int cat) consulta = consulta.Where(x => x.CategoriaId == cat || x.Categoria.CategoriaPaiId == cat);
        if (f.Tipo is { } tipo) consulta = consulta.Where(x => x.Tipo == tipo);
        if (f.Activo is bool activo) consulta = consulta.Where(x => x.Activo == activo);
        if (f.Disponivel is bool disp) consulta = consulta.Where(x => x.Disponivel == disp);

        var total = await consulta.CountAsync(ct);
        var ordenada = (f.Ordenar?.ToLowerInvariant(), f.Descendente) switch
        {
            ("codigo", false) => consulta.OrderBy(x => x.Codigo),
            ("codigo", true) => consulta.OrderByDescending(x => x.Codigo),
            ("preco", false) => consulta.OrderBy(x => x.PrecoVenda),
            ("preco", true) => consulta.OrderByDescending(x => x.PrecoVenda),
            ("categoria", false) => consulta.OrderBy(x => x.Categoria.Nome),
            ("categoria", true) => consulta.OrderByDescending(x => x.Categoria.Nome),
            (_, true) => consulta.OrderByDescending(x => x.Nome),
            _ => consulta.OrderBy(x => x.Nome),
        };
        var itens = await Projectar(ordenada.ThenBy(x => x.Id).Skip((pagina - 1) * tamanho).Take(tamanho)).ToListAsync(ct);
        return new Pagina<ProdutoDto>(itens, total, pagina, tamanho);
    }

    public async Task<ProdutoDto> ObterProdutoAsync(int id) =>
        await Projectar(db.Produtos.AsNoTracking().Where(p => p.Id == id)).FirstOrDefaultAsync()
        ?? throw new NaoEncontradoException("Produto não encontrado.");

    public async Task<ProdutoDto> CriarProdutoAsync(GuardarProduto dados)
    {
        if (dados.StockInicial is not null && !actual.TemPermissao(Permissoes.StockAjustar))
            throw new AcessoNegadoException("Lançar o stock inicial exige a permissão de ajustes de stock.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var produto = new Produto { Activo = true };
        await AplicarProduto(produto, dados);
        if (dados.StockInicial is { CustoUnitario: > 0 } s && produto.Custo == 0)
            produto.Custo = decimal.Round(s.CustoUnitario, 2, MidpointRounding.AwayFromZero);
        db.Produtos.Add(produto);
        await db.SaveChangesAsync();
        auditoria.Registar("produto.criar", "Produto", produto.Id, new { produto.Codigo, produto.Nome, produto.PrecoVenda });
        if (dados.StockInicial is { } stock)
            await LancarStockInicialAsync(produto, stock);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterProdutoAsync(produto.Id);
    }

    /// <summary>
    /// Produto de revenda com stock próprio: artigo em unidades com o mesmo código, ficha técnica 1:1 (a venda baixa o stock)
    /// e a entrada inicial (movimento «StockInicial») com o custo indicado, que passa a ser o custo médio.
    /// </summary>
    private async Task LancarStockInicialAsync(Produto produto, StockInicialProduto s)
    {
        if (await db.Artigos.AnyAsync(a => a.Codigo == produto.Codigo))
            throw new ConflitoException($"Já existe um artigo de stock com o código {produto.Codigo}. Use outro código no produto ou associe o artigo existente na ficha técnica.");
        var unidade = await db.UnidadesMedida.FirstOrDefaultAsync(u => u.Codigo == "un" && u.Activo)
                      ?? throw new RegraNegocioException("A unidade «un» não existe ou está inactiva.");
        var armazemId = s.ArmazemId ?? await db.Armazens.Where(a => a.Principal && a.Activo).Select(a => (int?)a.Id).FirstOrDefaultAsync()
                        ?? throw new RegraNegocioException("Não há armazém principal activo.");
        if (s.ArmazemId is int escolhido && !await db.Armazens.AnyAsync(a => a.Id == escolhido && a.Activo))
            throw new RegraNegocioException("Armazém inexistente ou inactivo.");

        var custo = decimal.Round(s.CustoUnitario, 4, MidpointRounding.AwayFromZero);
        var artigo = new Artigo
        {
            Codigo = produto.Codigo,
            Nome = produto.Nome,
            Tipo = TipoArtigo.Revenda,
            UnidadeMedidaId = unidade.Id,
            StockMinimo = decimal.Round(s.StockMinimo, 3),
            CustoMedio = custo,
            UltimoCusto = custo,
            Activo = true,
        };
        db.Artigos.Add(artigo);
        await db.SaveChangesAsync();
        db.Receitas.Add(new Receita
        {
            ProdutoId = produto.Id,
            Versao = 1,
            Activa = true,
            ValidaDesdeUtc = relogio.GetUtcNow().UtcDateTime,
            Observacoes = "Criada com o produto (revenda: 1 unidade por venda).",
            Itens = [new ReceitaItem { ArtigoId = artigo.Id, Quantidade = 1, UnidadeMedidaId = unidade.Id }],
        });
        if (s.Quantidade > 0)
            await motor.MovimentarAsync(artigo, armazemId, TipoMovimentoStock.StockInicial, s.Quantidade, custo, OrigemStockInicial, produto.Id, "Stock inicial");
        auditoria.Registar("produto.stock-inicial", "Produto", produto.Id,
            new { produto.Codigo, Artigo = artigo.Id, s.Quantidade, CustoUnitario = custo, Armazem = armazemId, s.StockMinimo });
    }

    public async Task<ProdutoDto> EditarProdutoAsync(int id, GuardarProduto dados)
    {
        var produto = await db.Produtos.FindAsync(id) ?? throw new NaoEncontradoException("Produto não encontrado.");
        db.VerificarVersao(produto, dados.RowVersion);
        if (dados.StockInicial is not null)
            throw new RegraNegocioException("O stock inicial só se lança na criação do produto. Para corrigir o stock use Stock → Ajuste.");

        var precoAnterior = produto.PrecoVenda;
        if (dados.PrecoVenda != precoAnterior && !actual.TemPermissao(Permissoes.ProdutosAlterarPreco))
            throw new RegraNegocioException("Não tem permissão para alterar preços de venda.");

        await AplicarProduto(produto, dados);
        if (produto.PrecoVenda != precoAnterior)
            auditoria.Registar("produto.alterar-preco", "Produto", id, new { produto.Codigo, Anterior = precoAnterior, Novo = produto.PrecoVenda });
        auditoria.Registar("produto.editar", "Produto", id, new { produto.Codigo, produto.Nome });
        await db.SaveChangesAsync();
        return await ObterProdutoAsync(id);
    }

    public async Task<ProdutoDto> DefinirProdutoActivoAsync(int id, bool activo, byte[]? versao)
    {
        var produto = await db.Produtos.FindAsync(id) ?? throw new NaoEncontradoException("Produto não encontrado.");
        db.VerificarVersao(produto, versao);
        if (activo && !await db.Categorias.AnyAsync(c => c.Id == produto.CategoriaId && c.Activo))
            throw new RegraNegocioException("A categoria do produto está inactiva. Active-a ou mude a categoria.");
        produto.Activo = activo;
        auditoria.Registar(activo ? "produto.activar" : "produto.desactivar", "Produto", id, new { produto.Codigo });
        await db.SaveChangesAsync();
        return await ObterProdutoAsync(id);
    }

    /// <summary>Marca o produto como esgotado/disponível (ex.: a cozinha ficou sem um prato).</summary>
    public async Task<ProdutoDto> DefinirDisponibilidadeAsync(int id, bool disponivel)
    {
        var produto = await db.Produtos.FindAsync(id) ?? throw new NaoEncontradoException("Produto não encontrado.");
        produto.Disponivel = disponivel;
        auditoria.Registar("produto.disponibilidade", "Produto", id, new { produto.Codigo, Disponivel = disponivel });
        await db.SaveChangesAsync();
        return await ObterProdutoAsync(id);
    }

    /// <summary>Sugere o próximo código no formato P0001 (o utilizador pode alterá-lo).</summary>
    public async Task<string> SugerirCodigoAsync()
    {
        var codigos = await db.Produtos.AsNoTracking().Where(p => p.Codigo.StartsWith("P")).Select(p => p.Codigo).ToListAsync();
        var maior = codigos.Select(c => int.TryParse(c[1..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return $"P{maior + 1:0000}";
    }

    private async Task AplicarProduto(Produto produto, GuardarProduto dados)
    {
        var codigo = dados.Codigo.Trim().ToUpperInvariant();
        var codigoBarras = string.IsNullOrWhiteSpace(dados.CodigoBarras) ? null : dados.CodigoBarras.Trim();

        if (await db.Produtos.AnyAsync(p => p.Id != produto.Id && p.Codigo == codigo))
            throw new ConflitoException($"Já existe um produto com o código {codigo}.");
        if (codigoBarras is not null && await db.Produtos.AnyAsync(p => p.Id != produto.Id && p.CodigoBarras == codigoBarras))
            throw new ConflitoException("Já existe um produto com este código de barras.");
        if (!await db.Categorias.AnyAsync(c => c.Id == dados.CategoriaId && c.Activo))
            throw new RegraNegocioException("Categoria inexistente ou inactiva.");
        if (dados.TaxaImpostoId is int taxa && !await db.TaxasImposto.AnyAsync(t => t.Id == taxa && t.Activo))
            throw new RegraNegocioException("Taxa de imposto inexistente ou inactiva.");
        if (dados.ImpressoraId != produto.ImpressoraId)
            await ValidarImpressoraTalaoAsync(dados.ImpressoraId);

        produto.ImpressoraId = dados.ImpressoraId;
        produto.Codigo = codigo;
        produto.CodigoBarras = codigoBarras;
        produto.Nome = dados.Nome.Trim();
        produto.Descricao = string.IsNullOrWhiteSpace(dados.Descricao) ? null : dados.Descricao.Trim();
        produto.CategoriaId = dados.CategoriaId;
        produto.Tipo = dados.Tipo;
        produto.PrecoVenda = decimal.Round(dados.PrecoVenda, 2, MidpointRounding.AwayFromZero);
        produto.Custo = decimal.Round(dados.Custo, 2, MidpointRounding.AwayFromZero);
        produto.TaxaImpostoId = dados.TaxaImpostoId;
        produto.ImagemUrl = string.IsNullOrWhiteSpace(dados.ImagemUrl) ? null : dados.ImagemUrl.Trim();
        produto.EnviaCozinha = dados.EnviaCozinha;
        produto.Disponivel = dados.Disponivel;
    }

    private static IQueryable<ProdutoDto> Projectar(IQueryable<Produto> consulta) => consulta.Select(p => new ProdutoDto(
        p.Id, p.Codigo, p.CodigoBarras, p.Nome, p.Descricao, p.CategoriaId, p.Categoria.Nome, p.Tipo, p.PrecoVenda, p.Custo,
        p.TaxaImpostoId, p.TaxaImposto != null ? p.TaxaImposto.Nome : null, p.TaxaImposto != null ? p.TaxaImposto.Percentagem : null,
        p.ImagemUrl, p.EnviaCozinha, p.Disponivel, p.Activo, p.RowVersion, p.ImpressoraId, p.Impressora != null ? p.Impressora.Nome : null));

    // ---------- Referências ----------

    public async Task<IReadOnlyList<TaxaImpostoDto>> ListarTaxasAsync(CancellationToken ct) =>
        await db.TaxasImposto.AsNoTracking().OrderBy(t => t.Percentagem)
            .Select(t => new TaxaImpostoDto(t.Id, t.Nome, t.Percentagem, t.Activo)).ToListAsync(ct);

    public async Task<IReadOnlyList<EstacaoPreparacaoDto>> ListarEstacoesAsync(CancellationToken ct) =>
        await db.EstacoesPreparacao.AsNoTracking().OrderBy(e => e.Nome)
            .Select(e => new EstacaoPreparacaoDto(e.Id, e.Nome, e.Activo)).ToListAsync(ct);

    public async Task<EstacaoPreparacaoDto> CriarEstacaoAsync(string nome)
    {
        nome = nome.Trim();
        if (nome.Length is < 2 or > 200)
            throw new RegraNegocioException("O nome da estação deve ter entre 2 e 200 caracteres.");
        if (await db.EstacoesPreparacao.AnyAsync(e => e.Nome == nome))
            throw new ConflitoException("Já existe uma estação com este nome.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var estacao = new EstacaoPreparacao { Nome = nome };
        db.EstacoesPreparacao.Add(estacao);
        await db.SaveChangesAsync();
        auditoria.Registar("estacao.criar", "EstacaoPreparacao", estacao.Id, new { nome });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return new EstacaoPreparacaoDto(estacao.Id, estacao.Nome, estacao.Activo);
    }
}
