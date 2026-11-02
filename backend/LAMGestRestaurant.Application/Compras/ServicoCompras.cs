using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Compras;
using LAMGestRestaurant.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Compras;

/// <summary>
/// Fornecedores e compras. Regras:
/// - custos das linhas sem IVA (IVA dedutível; o IVA é registado à parte e não entra no custo do stock);
/// - totais calculados no servidor; rascunhos editáveis e elimináveis; o stock só entra na confirmação, uma vez
///   (rowversion + estado + índice único de origem dos movimentos);
/// - a confirmação actualiza o custo médio ponderado e o último custo de cada artigo;
/// - a anulação de uma compra confirmada estorna o stock com movimentos contrários e falha se o stock
///   já tiver sido consumido (o custo médio não é recalculado retroactivamente).
/// </summary>
public class ServicoCompras(
    IAppDbContext db,
    IUtilizadorActual actual,
    IAuditoria auditoria,
    IConsultaUtilizadores utilizadores,
    MotorStock motor,
    ConfiguracoesSistema config,
    TimeProvider relogio)
{
    public const string OrigemCompraItem = "CompraItem";

    // ---------- Fornecedores ----------

    public async Task<IReadOnlyList<FornecedorDto>> ListarFornecedoresAsync(string? pesquisa, bool incluirInactivos, CancellationToken ct)
    {
        var q = db.Fornecedores.AsNoTracking().Where(f => incluirInactivos || f.Activo);
        if (!string.IsNullOrWhiteSpace(pesquisa))
            q = q.Where(f => f.Nome.Contains(pesquisa.Trim()) || f.Nuit == pesquisa.Trim() || f.Telefone!.Contains(pesquisa.Trim()));
        return await q.OrderBy(f => f.Nome)
            .Select(f => new FornecedorDto(f.Id, f.Nome, f.Nuit, f.Telefone, f.Email, f.Endereco, f.Observacoes, f.Activo,
                db.Compras.Count(c => c.FornecedorId == f.Id && c.Estado == EstadoCompra.Confirmada),
                db.Compras.Where(c => c.FornecedorId == f.Id && c.Estado == EstadoCompra.Confirmada).Sum(c => (decimal?)c.Total) ?? 0m))
            .ToListAsync(ct);
    }

    public async Task<FornecedorDto> GuardarFornecedorAsync(int? id, GuardarFornecedor dados)
    {
        var nuit = string.IsNullOrWhiteSpace(dados.Nuit) ? null : dados.Nuit.Trim();
        if (nuit is not null && await db.Fornecedores.AnyAsync(f => f.Id != id && f.Nuit == nuit))
            throw new ConflitoException("Já existe um fornecedor com este NUIT.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var fornecedor = id is int existente
            ? await db.Fornecedores.FindAsync(existente) ?? throw new NaoEncontradoException("Fornecedor não encontrado.")
            : db.Fornecedores.Add(new Fornecedor { Activo = true }).Entity;
        fornecedor.Nome = dados.Nome.Trim();
        fornecedor.Nuit = nuit;
        fornecedor.Telefone = Limpar(dados.Telefone);
        fornecedor.Email = Limpar(dados.Email);
        fornecedor.Endereco = Limpar(dados.Endereco);
        fornecedor.Observacoes = Limpar(dados.Observacoes);
        await db.SaveChangesAsync();
        auditoria.Registar(id is null ? "fornecedor.criar" : "fornecedor.editar", "Fornecedor", fornecedor.Id, new { fornecedor.Nome, fornecedor.Nuit });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return (await ListarFornecedoresAsync(null, true, default)).Single(f => f.Id == fornecedor.Id);
    }

    public async Task DefinirFornecedorActivoAsync(int id, bool activo)
    {
        var f = await db.Fornecedores.FindAsync(id) ?? throw new NaoEncontradoException("Fornecedor não encontrado.");
        if (!activo && await db.Compras.AnyAsync(c => c.FornecedorId == id && c.Estado == EstadoCompra.Rascunho))
            throw new RegraNegocioException("O fornecedor tem compras em rascunho. Confirme-as ou elimine-as primeiro.");
        f.Activo = activo;
        auditoria.Registar(activo ? "fornecedor.activar" : "fornecedor.desactivar", "Fornecedor", id, new { f.Nome });
        await db.SaveChangesAsync();
    }

    // ---------- Compras ----------

    public async Task<Pagina<CompraResumo>> ListarAsync(FiltroCompras f, CancellationToken ct)
    {
        var (pagina, tamanho) = Pagina<CompraResumo>.Normalizar(f.Pagina, f.Tamanho);
        var q = db.Compras.AsNoTracking();
        if (f.FornecedorId is int forn) q = q.Where(c => c.FornecedorId == forn);
        if (f.Estado is { } estado) q = q.Where(c => c.Estado == estado);
        if (f.Desde is DateOnly desde) q = q.Where(c => c.DataDocumento >= desde);
        if (f.Ate is DateOnly ate) q = q.Where(c => c.DataDocumento <= ate);
        if (!string.IsNullOrWhiteSpace(f.Pesquisa)) q = q.Where(c => c.NumeroDocumento.Contains(f.Pesquisa.Trim()) || c.Fornecedor.Nome.Contains(f.Pesquisa.Trim()));
        var total = await q.CountAsync(ct);
        var itens = await q.OrderByDescending(c => c.DataDocumento).ThenByDescending(c => c.Id).Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(c => new CompraResumo(c.Id, c.Fornecedor.Nome, c.NumeroDocumento, c.DataDocumento, c.Estado, c.Total, c.Itens.Count))
            .ToListAsync(ct);
        return new Pagina<CompraResumo>(itens, total, pagina, tamanho);
    }

    public async Task<CompraDto> ObterAsync(int id)
    {
        var c = await db.Compras.AsNoTracking().Include(x => x.Fornecedor).Include(x => x.Armazem)
            .Include(x => x.Itens).ThenInclude(i => i.Artigo).Include(x => x.Itens).ThenInclude(i => i.UnidadeMedida)
            .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id) ?? throw new NaoEncontradoException("Compra não encontrada.");
        var nomes = await utilizadores.NomesAsync(new[] { c.CriadoPorId, c.ConfirmadaPorId }.Where(x => x is not null).Select(x => x!.Value));
        return new CompraDto(c.Id, c.FornecedorId, c.Fornecedor.Nome, c.NumeroDocumento, c.DataDocumento, c.DataVencimento, c.ArmazemId, c.Armazem.Nome,
            c.Estado, c.Subtotal, c.Desconto, c.Impostos, c.Total, c.Observacoes,
            c.CriadoPorId is int cp ? nomes.GetValueOrDefault(cp) : null, c.ConfirmadaEmUtc, c.ConfirmadaPorId is int cf ? nomes.GetValueOrDefault(cf) : null,
            c.AnuladaEmUtc, c.MotivoAnulacao,
            [.. c.Itens.OrderBy(i => i.Id).Select(i => new CompraItemDto(i.Id, i.ArtigoId, i.Artigo.Codigo, i.Artigo.Nome, i.Quantidade, i.UnidadeMedidaId,
                i.UnidadeMedida.Codigo, i.CustoUnitario, i.Desconto, i.TaxaImpostoPercentagem, i.ValorImposto, i.Total))],
            c.RowVersion);
    }

    public async Task<CompraDto> CriarRascunhoAsync(GuardarCompra dados)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var compra = new Compra();
        await AplicarAsync(compra, dados);
        db.Compras.Add(compra);
        await GravarComDuplicadoAsync();
        auditoria.Registar("compra.criar", "Compra", compra.Id, new { compra.NumeroDocumento, compra.FornecedorId, compra.Total });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(compra.Id);
    }

    public async Task<CompraDto> EditarRascunhoAsync(int id, GuardarCompra dados)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var compra = await CarregarRascunhoAsync(id, dados.RowVersion);
        db.CompraItens.RemoveRange(compra.Itens);
        compra.Itens.Clear();
        await AplicarAsync(compra, dados);
        await GravarComDuplicadoAsync();
        auditoria.Registar("compra.editar", "Compra", id, new { compra.NumeroDocumento, compra.Total });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(id);
    }

    /// <summary>Só rascunhos podem ser eliminados (não movimentaram stock nem têm valor contabilístico).</summary>
    public async Task EliminarRascunhoAsync(int id, byte[]? versao)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var compra = await CarregarRascunhoAsync(id, versao);
        auditoria.Registar("compra.eliminar-rascunho", "Compra", id, new { compra.NumeroDocumento, compra.FornecedorId, compra.Total });
        db.Compras.Remove(compra);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
    }

    public async Task<CompraDto> ConfirmarAsync(int id, byte[]? versao)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var compra = await CarregarRascunhoAsync(id, versao);
        var artigoIds = compra.Itens.Select(i => i.ArtigoId).Distinct().ToList();
        var artigos = await db.Artigos.Include(a => a.UnidadeMedida).Where(a => artigoIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        var unidades = await db.UnidadesMedida.ToDictionaryAsync(u => u.Id);

        foreach (var item in compra.Itens.OrderBy(i => i.Id))
        {
            var artigo = artigos[item.ArtigoId];
            if (!artigo.Activo)
                throw new RegraNegocioException($"{artigo.Nome} está inactivo.");
            var quantidade = decimal.Round(Unidades.Converter(item.Quantidade, unidades[item.UnidadeMedidaId], artigo.UnidadeMedida, artigo.Nome), 3, MidpointRounding.AwayFromZero);
            var liquido = item.Total - item.ValorImposto;
            var custoUnitario = quantidade == 0 ? 0 : decimal.Round(liquido / quantidade, 4, MidpointRounding.AwayFromZero);

            // Custo médio ponderado com o stock existente (todos os armazéns). Sem stock positivo, assume o custo desta compra.
            var saldoAntes = await motor.SaldoTotalAsync(artigo.Id);
            artigo.CustoMedio = saldoAntes <= 0
                ? custoUnitario
                : decimal.Round((saldoAntes * artigo.CustoMedio + quantidade * custoUnitario) / (saldoAntes + quantidade), 4, MidpointRounding.AwayFromZero);
            artigo.UltimoCusto = custoUnitario;
            await motor.MovimentarAsync(artigo, compra.ArmazemId, TipoMovimentoStock.EntradaCompra, quantidade, custoUnitario,
                OrigemCompraItem, item.Id, $"Compra {compra.NumeroDocumento}");
        }

        compra.Estado = EstadoCompra.Confirmada;
        compra.ConfirmadaEmUtc = relogio.GetUtcNow().UtcDateTime;
        compra.ConfirmadaPorId = actual.Id;
        auditoria.Registar("compra.confirmar", "Compra", id, new { compra.NumeroDocumento, compra.FornecedorId, compra.Total, Linhas = compra.Itens.Count });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(id);
    }

    public async Task<CompraDto> AnularAsync(int id, string motivo, byte[]? versao)
    {
        if (!actual.TemPermissao(Permissoes.ComprasAnular))
            throw new AcessoNegadoException("Não tem permissão para anular compras confirmadas.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var compra = await db.Compras.Include(c => c.Itens).FirstOrDefaultAsync(c => c.Id == id) ?? throw new NaoEncontradoException("Compra não encontrada.");
        db.VerificarVersao(compra, versao);
        if (compra.Estado != EstadoCompra.Confirmada)
            throw new RegraNegocioException(compra.Estado == EstadoCompra.Rascunho ? "Rascunhos eliminam-se; não se anulam." : "A compra já está anulada.");

        var entradas = await db.MovimentosStock
            .Where(m => m.Tipo == TipoMovimentoStock.EntradaCompra && m.OrigemTipo == OrigemCompraItem && compra.Itens.Select(i => (int?)i.Id).Contains(m.OrigemId))
            .ToListAsync();
        var artigos = await db.Artigos.Where(a => entradas.Select(e => e.ArtigoId).Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        foreach (var entrada in entradas)
        {
            try
            {
                await motor.MovimentarAsync(artigos[entrada.ArtigoId], entrada.ArmazemId, TipoMovimentoStock.EstornoCompra, -entrada.Quantidade,
                    entrada.CustoUnitario, OrigemCompraItem, entrada.OrigemId, $"Anulação da compra {compra.NumeroDocumento}: {motivo.Trim()}");
            }
            catch (RegraNegocioException ex)
            {
                throw new RegraNegocioException($"Não é possível anular: parte do stock desta compra já foi consumida. {ex.Message}");
            }
        }
        compra.Estado = EstadoCompra.Anulada;
        compra.AnuladaEmUtc = relogio.GetUtcNow().UtcDateTime;
        compra.AnuladaPorId = actual.Id;
        compra.MotivoAnulacao = motivo.Trim();
        auditoria.Registar("compra.anular", "Compra", id, new { compra.NumeroDocumento, compra.Total, Motivo = compra.MotivoAnulacao });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(id);
    }

    public async Task<ResumoCompras> ResumoAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        if (ate < desde) throw new RegraNegocioException("A data final é anterior à inicial.");
        var compras = db.Compras.AsNoTracking().Where(c => c.Estado == EstadoCompra.Confirmada && c.DataDocumento >= desde && c.DataDocumento <= ate);
        var porFornecedor = await compras.GroupBy(c => c.Fornecedor.Nome)
            .Select(g => new LinhaValor(g.Key, g.Sum(c => c.Total), g.Count())).ToListAsync(ct);
        return new ResumoCompras(desde, ate, porFornecedor.Sum(l => l.Valor), porFornecedor.Sum(l => l.Quantidade),
            [.. porFornecedor.OrderByDescending(l => l.Valor)]);
    }

    // ---------- Auxiliares ----------

    private async Task AplicarAsync(Compra compra, GuardarCompra dados)
    {
        if (!await db.Fornecedores.AnyAsync(f => f.Id == dados.FornecedorId && f.Activo))
            throw new RegraNegocioException("Fornecedor inexistente ou inactivo.");
        if (!await db.Armazens.AnyAsync(a => a.Id == dados.ArmazemId && a.Activo))
            throw new RegraNegocioException("Armazém inexistente ou inactivo.");
        if (dados.DataVencimento is DateOnly venc && venc < dados.DataDocumento)
            throw new RegraNegocioException("A data de vencimento é anterior à data do documento.");
        var hoje = await config.HojeAsync(relogio);
        if (dados.DataDocumento > hoje)
            throw new RegraNegocioException("A data do documento não pode ser futura.");

        var artigoIds = dados.Itens.Select(i => i.ArtigoId).Distinct().ToList();
        var artigos = await db.Artigos.Include(a => a.UnidadeMedida).Where(a => artigoIds.Contains(a.Id) && a.Activo).ToDictionaryAsync(a => a.Id);
        var unidades = await db.UnidadesMedida.ToDictionaryAsync(u => u.Id);

        compra.FornecedorId = dados.FornecedorId;
        compra.NumeroDocumento = dados.NumeroDocumento.Trim();
        compra.DataDocumento = dados.DataDocumento;
        compra.DataVencimento = dados.DataVencimento;
        compra.ArmazemId = dados.ArmazemId;
        compra.Observacoes = Limpar(dados.Observacoes);
        compra.Estado = EstadoCompra.Rascunho;

        foreach (var l in dados.Itens)
        {
            if (!artigos.TryGetValue(l.ArtigoId, out var artigo))
                throw new RegraNegocioException("Artigo inexistente ou inactivo na compra.");
            if (!unidades.TryGetValue(l.UnidadeMedidaId, out var unidade) || unidade.Dimensao != artigo.UnidadeMedida.Dimensao)
                throw new RegraNegocioException($"Unidade inválida para {artigo.Nome} (gerido em {artigo.UnidadeMedida.Codigo}).");
            var bruto = Arred(l.Quantidade * l.CustoUnitario);
            var desconto = Arred(l.Desconto);
            if (desconto > bruto)
                throw new RegraNegocioException($"O desconto da linha {artigo.Nome} excede o valor da linha.");
            var imposto = Arred((bruto - desconto) * l.TaxaImpostoPercentagem / 100m);
            compra.Itens.Add(new CompraItem
            {
                ArtigoId = artigo.Id,
                Quantidade = decimal.Round(l.Quantidade, 3, MidpointRounding.AwayFromZero),
                UnidadeMedidaId = unidade.Id,
                CustoUnitario = decimal.Round(l.CustoUnitario, 4, MidpointRounding.AwayFromZero),
                Desconto = desconto,
                TaxaImpostoPercentagem = l.TaxaImpostoPercentagem,
                ValorImposto = imposto,
                Total = bruto - desconto + imposto,
            });
        }
        compra.Subtotal = compra.Itens.Sum(i => i.Total - i.ValorImposto + i.Desconto);
        compra.Desconto = compra.Itens.Sum(i => i.Desconto);
        compra.Impostos = compra.Itens.Sum(i => i.ValorImposto);
        compra.Total = compra.Itens.Sum(i => i.Total);
    }

    private async Task GravarComDuplicadoAsync()
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            throw new ConflitoException("Este documento do fornecedor já está registado noutra compra.");
        }
    }

    private async Task<Compra> CarregarRascunhoAsync(int id, byte[]? versao)
    {
        var compra = await db.Compras.Include(c => c.Itens).FirstOrDefaultAsync(c => c.Id == id) ?? throw new NaoEncontradoException("Compra não encontrada.");
        db.VerificarVersao(compra, versao);
        if (compra.Estado != EstadoCompra.Rascunho)
            throw new RegraNegocioException("Só compras em rascunho podem ser alteradas, confirmadas ou eliminadas.");
        compra.AlteradoEmUtc = relogio.GetUtcNow().UtcDateTime;
        return compra;
    }

    private static decimal Arred(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);
    private static string? Limpar(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
}
