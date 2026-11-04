using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Catalogo;

/// <summary>
/// Stock dos produtos de venda. Um produto que controla stock e ainda não tem ficha técnica recebe um artigo de stock
/// (revenda, em unidades, com o mesmo código e nome) e uma ficha técnica 1:1: aparece no Stock, nos inventários e nos
/// movimentos, e cada venda baixa uma unidade. Produtos com ficha técnica de ingredientes continuam a baixar os ingredientes.
/// Sem stock inicial, o artigo permite stock negativo: as vendas nunca ficam bloqueadas e o stock acerta-se com uma
/// compra, um ajuste ou um inventário.
/// </summary>
public class ServicoStockProdutos(IAppDbContext db, MotorStock motor, IAuditoria auditoria, TimeProvider relogio)
{
    public const string ObservacaoFicha = "Criada com o produto (revenda: 1 unidade por venda).";

    /// <summary>
    /// Cria o artigo e a ficha técnica 1:1 do produto, se ele nunca teve ficha técnica (dentro da transacção do chamador).
    /// Devolve o artigo criado (ou reaproveitado), ou null se o produto já tem ficha técnica.
    /// </summary>
    internal async Task<Artigo?> GarantirAsync(Produto produto, bool permiteNegativo, decimal stockMinimo = 0, decimal custo = 0)
    {
        if (await db.Receitas.AnyAsync(r => r.ProdutoId == produto.Id))
            return null;
        var unidade = await db.UnidadesMedida.FirstOrDefaultAsync(u => u.Codigo == "un" && u.Activo)
                      ?? throw new RegraNegocioException("A unidade «un» não existe ou está inactiva.");

        var artigo = await ArtigoLivreComCodigoAsync(produto.Codigo);
        if (artigo is null)
        {
            artigo = new Artigo
            {
                Codigo = await CodigoLivreAsync(produto.Codigo),
                Nome = produto.Nome,
                Tipo = TipoArtigo.Revenda,
                UnidadeMedidaId = unidade.Id,
                StockMinimo = decimal.Round(stockMinimo, 3),
                CustoMedio = custo,
                UltimoCusto = custo,
                PermiteStockNegativo = permiteNegativo,
                Activo = true,
            };
            db.Artigos.Add(artigo);
            await db.SaveChangesAsync();
        }
        db.Receitas.Add(new Receita
        {
            ProdutoId = produto.Id,
            Versao = 1,
            Activa = true,
            ValidaDesdeUtc = relogio.GetUtcNow().UtcDateTime,
            Observacoes = ObservacaoFicha,
            Itens = [new ReceitaItem { ArtigoId = artigo.Id, Quantidade = 1, UnidadeMedidaId = artigo.UnidadeMedidaId }],
        });
        auditoria.Registar("produto.stock-activar", "Produto", produto.Id, new { produto.Codigo, Artigo = artigo.Codigo, PermiteStockNegativo = permiteNegativo });
        return artigo;
    }

    /// <summary>Entrada inicial de stock (movimento «StockInicial»); o custo passa a ser o custo médio.</summary>
    internal async Task LancarStockInicialAsync(Artigo artigo, Produto produto, decimal quantidade, decimal custo, int? armazemId)
    {
        var armazem = armazemId ?? await db.Armazens.Where(a => a.Principal && a.Activo).Select(a => (int?)a.Id).FirstOrDefaultAsync()
                      ?? throw new RegraNegocioException("Não há armazém principal activo.");
        if (armazemId is int escolhido && !await db.Armazens.AnyAsync(a => a.Id == escolhido && a.Activo))
            throw new RegraNegocioException("Armazém inexistente ou inactivo.");
        if (quantidade > 0)
            await motor.MovimentarAsync(artigo, armazem, TipoMovimentoStock.StockInicial, quantidade, custo, ServicoCatalogo.OrigemStockInicial, produto.Id, "Stock inicial");
        auditoria.Registar("produto.stock-inicial", "Produto", produto.Id,
            new { produto.Codigo, Artigo = artigo.Id, Quantidade = quantidade, CustoUnitario = custo, Armazem = armazem, artigo.StockMinimo });
    }

    /// <summary>
    /// Activa o stock de todos os produtos que o controlam e ainda não têm ficha técnica (produtos criados antes desta regra).
    /// Idempotente: corre no arranque do servidor. Devolve quantos produtos foram activados.
    /// </summary>
    public async Task<int> SincronizarTodosAsync(CancellationToken ct = default)
    {
        var produtos = await db.Produtos.Where(p => p.ControlaStock && !db.Receitas.Any(r => r.ProdutoId == p.Id)).OrderBy(p => p.Id).ToListAsync(ct);
        if (produtos.Count == 0)
            return 0;
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        foreach (var p in produtos)
            await GarantirAsync(p, permiteNegativo: true, custo: decimal.Round(p.Custo, 4, MidpointRounding.AwayFromZero));
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return produtos.Count;
    }

    /// <summary>Mantém o nome do artigo 1:1 igual ao do produto (só o artigo criado automaticamente com o produto).</summary>
    internal async Task SincronizarNomeAsync(Produto produto)
    {
        var artigo = await db.Receitas.Where(r => r.ProdutoId == produto.Id && r.Activa && r.Observacoes == ObservacaoFicha && r.Itens.Count == 1)
            .Select(r => r.Itens.First().Artigo).FirstOrDefaultAsync();
        if (artigo is not null && artigo.Nome != produto.Nome)
            artigo.Nome = produto.Nome;
    }

    /// <summary>Artigo de revenda com o mesmo código que ainda não está em nenhuma ficha técnica (pode ser reaproveitado).</summary>
    private Task<Artigo?> ArtigoLivreComCodigoAsync(string codigo) =>
        db.Artigos.FirstOrDefaultAsync(a => a.Codigo == codigo && a.Tipo == TipoArtigo.Revenda && !db.ReceitaItens.Any(i => i.ArtigoId == a.Id));

    /// <summary>O código do produto ou, se já for de outro artigo (ex.: um ingrediente), «código-P», «código-P2»…</summary>
    private async Task<string> CodigoLivreAsync(string codigo)
    {
        if (!await db.Artigos.AnyAsync(a => a.Codigo == codigo))
            return codigo;
        for (var n = 1; ; n++)
        {
            var sufixo = n == 1 ? "-P" : $"-P{n}";
            var candidato = (codigo.Length + sufixo.Length > 30 ? codigo[..(30 - sufixo.Length)] : codigo) + sufixo;
            if (!await db.Artigos.AnyAsync(a => a.Codigo == candidato))
                return candidato;
        }
    }
}
