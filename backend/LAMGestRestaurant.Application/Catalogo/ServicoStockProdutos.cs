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
public class ServicoStockProdutos(IAppDbContext db, MotorStock motor, IAuditoria auditoria, TimeProvider relogio, ArmazensAbate abate)
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
        // Na unidade em que o produto é vendido: 1 venda de 1 kg baixa 1 kg.
        var unidade = await db.UnidadesMedida.FirstOrDefaultAsync(u => produto.UnidadeMedidaId != null ? u.Id == produto.UnidadeMedidaId : u.Codigo == "un" && u.Activo)
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
                CategoriaId = produto.CategoriaId,
                ArmazemId = produto.ArmazemId,
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
        // Sem armazém indicado, entra onde as vendas deste produto vão baixar.
        var armazem = armazemId ?? (await abate.ResolverAsync([produto.Id]))[produto.Id];
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

    /// <summary>
    /// Produto mudou de unidade (o artigo automático, da ficha 1:1, acompanha-a). Fichas técnicas de ingredientes não mudam
    /// (a unidade do produto é só a de venda). Os movimentos já registados nunca mudam:
    /// <list type="bullet">
    /// <item>sem movimentos: o artigo passa a ser gerido na unidade nova;</item>
    /// <item>com movimentos e unidade da mesma natureza (ex.: kg → g): o stock continua na unidade do artigo e a ficha converte
    /// (1 venda = 1 g = 0,001 kg);</item>
    /// <item>com movimentos e natureza diferente (ex.: un → kg): com o stock a zero, nasce um artigo novo na unidade nova
    /// (o antigo fica inactivo com o histórico); com stock, é recusado até o stock ser acertado para zero.</item>
    /// </list>
    /// Chamar dentro de uma transacção (grava a meio, para a nova versão da ficha).
    /// </summary>
    internal async Task MudarUnidadeAsync(Produto produto, int unidadeId)
    {
        var receita = await db.Receitas.Include(r => r.Itens).ThenInclude(i => i.Artigo).ThenInclude(a => a.UnidadeMedida)
            .FirstOrDefaultAsync(r => r.ProdutoId == produto.Id && r.Activa && r.Observacoes == ObservacaoFicha && r.Itens.Count == 1);
        if (receita is null)
            return;
        var linha = receita.Itens[0];
        var artigo = linha.Artigo;
        if (linha.UnidadeMedidaId == unidadeId && artigo.UnidadeMedidaId == unidadeId)
            return;
        var nova = await db.UnidadesMedida.FirstAsync(u => u.Id == unidadeId);
        if (!await db.MovimentosStock.AnyAsync(m => m.ArtigoId == artigo.Id))
        {
            artigo.UnidadeMedidaId = unidadeId;
            linha.UnidadeMedidaId = unidadeId;
            return;
        }
        if (nova.Dimensao == artigo.UnidadeMedida.Dimensao)
        {
            linha.UnidadeMedidaId = unidadeId;
            return;
        }

        var comStock = await db.StockSaldos.Where(s => s.ArtigoId == artigo.Id && s.Quantidade != 0)
            .Select(s => new { s.Quantidade, Armazem = s.Armazem.Nome }).ToListAsync();
        if (comStock.Count > 0)
            throw new RegraNegocioException(
                $"Não é possível passar {produto.Nome} de {artigo.UnidadeMedida.Codigo} para {nova.Codigo} com stock: " +
                $"{string.Join(", ", comStock.Select(s => $"{s.Quantidade:0.###} {artigo.UnidadeMedida.Codigo} em {s.Armazem}"))}. " +
                "Acerte primeiro o stock para zero (Stock → Artigos → Ajuste) e volte a mudar a unidade.",
                nameof(GuardarProduto.UnidadeMedidaId));

        var agora = relogio.GetUtcNow().UtcDateTime;
        var novo = new Artigo
        {
            Codigo = await CodigoLivreAsync(produto.Codigo),
            Nome = produto.Nome,
            Tipo = artigo.Tipo,
            UnidadeMedidaId = unidadeId,
            CustoMedio = decimal.Round(produto.Custo, 4, MidpointRounding.AwayFromZero),
            UltimoCusto = decimal.Round(produto.Custo, 4, MidpointRounding.AwayFromZero),
            PermiteStockNegativo = artigo.PermiteStockNegativo,
            CategoriaId = artigo.CategoriaId,
            ArmazemId = artigo.ArmazemId,
            Localizacao = artigo.Localizacao,
            Activo = true,
        };
        db.Artigos.Add(novo);
        // Encerra a ficha e o artigo antigos (o histórico dos movimentos fica no artigo antigo, na unidade dele).
        receita.Activa = false;
        receita.ValidaAteUtc = agora;
        artigo.Activo = false;
        await db.SaveChangesAsync();
        db.Receitas.Add(new Receita
        {
            ProdutoId = produto.Id,
            Versao = await db.Receitas.Where(r => r.ProdutoId == produto.Id).MaxAsync(r => r.Versao) + 1,
            Activa = true,
            ValidaDesdeUtc = agora,
            Observacoes = ObservacaoFicha,
            Itens = [new ReceitaItem { ArtigoId = novo.Id, Quantidade = 1, UnidadeMedidaId = unidadeId }],
        });
        auditoria.Registar("produto.mudar-unidade", "Produto", produto.Id,
            new { produto.Codigo, De = artigo.UnidadeMedida.Codigo, Para = nova.Codigo, ArtigoAnterior = artigo.Codigo, ArtigoNovo = novo.Codigo });
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
