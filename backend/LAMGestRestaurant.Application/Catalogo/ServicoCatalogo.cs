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
public class ServicoCatalogo(IAppDbContext db, IUtilizadorActual actual, IAuditoria auditoria, ServicoStockProdutos stockProdutos)
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
        await db.Impressoras.AsNoTracking().Where(i => i.Funcao != FuncaoImpressora.Caixa && i.Funcao != FuncaoImpressora.Etiquetas)
            .OrderBy(i => i.Funcao).ThenBy(i => i.Nome)
            .Select(i => new ImpressoraTalao(i.Id, i.Nome, i.Funcao, i.Activo)).ToListAsync(ct);

    /// <summary>Só impressoras de talões (Bar/Cozinha) activas; as de caixa imprimem recibos.</summary>
    private async Task ValidarImpressoraTalaoAsync(int? impressoraId)
    {
        if (impressoraId is int id && !await db.Impressoras.AnyAsync(i => i.Id == id && i.Activo && i.Funcao != FuncaoImpressora.Caixa && i.Funcao != FuncaoImpressora.Etiquetas))
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
        if (dados.StockInicial is { Quantidade: > 0 } && !actual.TemPermissao(Permissoes.StockAjustar))
            throw new AcessoNegadoException("Lançar o stock inicial exige a permissão de ajustes de stock.");
        if (dados.StockInicial is not null && !dados.ControlaStock)
            throw new RegraNegocioException("Um produto que não controla stock não leva stock inicial.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var produto = await CriarProdutoEmTransacaoAsync(dados);
        await transacao.CommitAsync();
        return await ObterProdutoAsync(produto.Id);
    }

    /// <summary>Cria o produto com o artigo de stock, a ficha técnica e o stock inicial, dentro da transacção do chamador.</summary>
    internal async Task<Produto> CriarProdutoEmTransacaoAsync(GuardarProduto dados)
    {
        var produto = new Produto { Activo = true };
        await AplicarProduto(produto, dados);
        if (dados.StockInicial is { CustoUnitario: > 0 } s && produto.Custo == 0)
            produto.Custo = decimal.Round(s.CustoUnitario, 2, MidpointRounding.AwayFromZero);
        db.Produtos.Add(produto);
        await db.SaveChangesAsync();
        auditoria.Registar("produto.criar", "Produto", produto.Id, new { produto.Codigo, produto.Nome, produto.PrecoVenda });
        if (produto.ControlaStock)
        {
            // Stock próprio do produto (artigo + ficha técnica 1:1). Sem quantidade inicial, as vendas podem pôr o stock negativo.
            var s0 = dados.StockInicial;
            var custo = decimal.Round(s0?.CustoUnitario ?? produto.Custo, 4, MidpointRounding.AwayFromZero);
            var artigo = await stockProdutos.GarantirAsync(produto, permiteNegativo: s0 is not { Quantidade: > 0 }, s0?.StockMinimo ?? 0, custo);
            if (artigo is not null && s0 is { } stock)
                await stockProdutos.LancarStockInicialAsync(artigo, produto, stock.Quantidade, custo, stock.ArmazemId);
        }
        await db.SaveChangesAsync();
        return produto;
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

        var controlavaStock = produto.ControlaStock;
        var unidadeAnterior = produto.UnidadeMedidaId;
        await AplicarProduto(produto, dados);
        if (produto.UnidadeMedidaId is int novaUnidade && novaUnidade != unidadeAnterior)
            await stockProdutos.MudarUnidadeAsync(produto, novaUnidade);
        if (produto.ControlaStock && !controlavaStock)
            await stockProdutos.GarantirAsync(produto, permiteNegativo: true, custo: decimal.Round(produto.Custo, 4, MidpointRounding.AwayFromZero));
        if (produto.ControlaStock != controlavaStock)
            auditoria.Registar("produto.controla-stock", "Produto", id, new { produto.Codigo, produto.ControlaStock });
        await stockProdutos.SincronizarNomeAsync(produto);
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

    /// <summary>
    /// Gera um código de barras EAN-13 de uso interno (prefixo 20, reservado a códigos da própria loja) que nenhum produto usa.
    /// Só se grava quando o produto é guardado; o índice único da BD continua a impedir repetidos.
    /// </summary>
    public async Task<string> GerarCodigoBarrasAsync(CancellationToken ct, IReadOnlySet<string>? reservados = null)
    {
        for (var tentativa = 0; tentativa < 50; tentativa++)
        {
            var codigo = Ean13("20" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1_000_000_000).ToString("0000000000"));
            if (reservados?.Contains(codigo) != true && !await db.Produtos.AnyAsync(p => p.CodigoBarras == codigo, ct)) return codigo;
        }
        throw new RegraNegocioException("Não foi possível gerar um código de barras livre. Tente novamente.");
    }

    /// <summary>Junta o dígito de controlo EAN-13 aos 12 dígitos dados.</summary>
    public static string Ean13(string doze)
    {
        if (doze.Length != 12 || !doze.All(char.IsAsciiDigit)) throw new ArgumentException("São precisos 12 dígitos.", nameof(doze));
        var soma = doze.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return doze + (char)('0' + (10 - soma % 10) % 10);
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
        var unidadeId = dados.UnidadeMedidaId ?? produto.UnidadeMedidaId
            ?? await db.UnidadesMedida.Where(u => u.Codigo == "un" && u.Activo).Select(u => (int?)u.Id).FirstOrDefaultAsync();
        if (unidadeId != produto.UnidadeMedidaId && unidadeId is int u1 && !await db.UnidadesMedida.AnyAsync(u => u.Id == u1 && u.Activo))
            throw new RegraNegocioException("Unidade inexistente ou inactiva.");
        if (dados.ArmazemId != produto.ArmazemId && dados.ArmazemId is int a1 && !await db.Armazens.AnyAsync(a => a.Id == a1 && a.Activo))
            throw new RegraNegocioException("Armazém inexistente ou inactivo.");

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
        produto.ControlaStock = dados.ControlaStock;
        produto.UnidadeMedidaId = unidadeId;
        produto.ArmazemId = dados.ArmazemId;
        produto.PedeQuantidade = dados.PedeQuantidade;
    }

    private IQueryable<ProdutoDto> Projectar(IQueryable<Produto> consulta) => consulta.Select(p => new ProdutoDto(
        p.Id, p.Codigo, p.CodigoBarras, p.Nome, p.Descricao, p.CategoriaId, p.Categoria.Nome, p.Tipo, p.PrecoVenda, p.Custo,
        p.TaxaImpostoId, p.TaxaImposto != null ? p.TaxaImposto.Nome : null, p.TaxaImposto != null ? p.TaxaImposto.Percentagem : null,
        p.ImagemUrl, p.EnviaCozinha, p.Disponivel, p.Activo, p.RowVersion, p.ImpressoraId, p.Impressora != null ? p.Impressora.Nome : null,
        p.ControlaStock,
        db.Receitas.Where(r => r.ProdutoId == p.Id && r.Activa && r.Itens.Count == 1).Select(r => (int?)r.Itens.First().ArtigoId).FirstOrDefault(),
        p.UnidadeMedidaId, p.UnidadeMedida != null ? p.UnidadeMedida.Codigo : "un", p.ArmazemId, p.Armazem != null ? p.Armazem.Nome : null, p.PedeQuantidade));

    // ---------- Referências ----------

    public async Task<IReadOnlyList<TaxaImpostoDto>> ListarTaxasAsync(CancellationToken ct) =>
        await db.TaxasImposto.AsNoTracking().OrderBy(t => t.Percentagem)
            .Select(t => new TaxaImpostoDto(t.Id, t.Nome, t.Percentagem, t.Activo)).ToListAsync(ct);

    public async Task<IReadOnlyList<EstacaoPreparacaoDto>> ListarEstacoesAsync(CancellationToken ct) =>
        await db.EstacoesPreparacao.AsNoTracking().OrderBy(e => e.Nome)
            .Select(e => new EstacaoPreparacaoDto(e.Id, e.Nome, e.Activo, e.ArmazemId, e.Armazem != null ? e.Armazem.Nome : null)).ToListAsync(ct);

    /// <summary>Armazém de onde sai o stock vendido por este centro de produção (vazio = o armazém do produto).</summary>
    public async Task<EstacaoPreparacaoDto> DefinirArmazemEstacaoAsync(int id, int? armazemId)
    {
        var estacao = await db.EstacoesPreparacao.FindAsync(id) ?? throw new NaoEncontradoException("Centro de produção não encontrado.");
        var nome = armazemId is int a
            ? await db.Armazens.Where(x => x.Id == a && x.Activo).Select(x => x.Nome).FirstOrDefaultAsync() ?? throw new RegraNegocioException("Armazém inexistente ou inactivo.")
            : null;
        auditoria.Registar("estacao.armazem", "EstacaoPreparacao", id, new { estacao.Nome, Anterior = estacao.ArmazemId, Novo = armazemId });
        estacao.ArmazemId = armazemId;
        await db.SaveChangesAsync();
        return new EstacaoPreparacaoDto(estacao.Id, estacao.Nome, estacao.Activo, armazemId, nome);
    }

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
