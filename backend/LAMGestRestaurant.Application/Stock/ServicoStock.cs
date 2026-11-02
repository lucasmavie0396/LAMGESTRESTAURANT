using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Stock;

/// <summary>
/// Artigos de stock, movimentos, ajustes, perdas, transferências e inventários físicos.
/// Os movimentos são imutáveis; correcções fazem-se com novos movimentos e ficam auditadas.
/// </summary>
public class ServicoStock(
    IAppDbContext db,
    IUtilizadorActual actual,
    IAuditoria auditoria,
    IConsultaUtilizadores utilizadores,
    MotorStock motor)
{
    // ---------- Referências ----------

    public async Task<IReadOnlyList<UnidadeDto>> UnidadesAsync(CancellationToken ct) =>
        await db.UnidadesMedida.AsNoTracking().Where(u => u.Activo).OrderBy(u => u.Dimensao).ThenByDescending(u => u.FactorParaBase)
            .Select(u => new UnidadeDto(u.Id, u.Codigo, u.Nome, u.Dimensao, u.FactorParaBase)).ToListAsync(ct);

    public async Task<IReadOnlyList<ArmazemDto>> ArmazensAsync(CancellationToken ct) =>
        await db.Armazens.AsNoTracking().OrderByDescending(a => a.Principal).ThenBy(a => a.Nome)
            .Select(a => new ArmazemDto(a.Id, a.Nome, a.Principal, a.Activo)).ToListAsync(ct);

    public async Task<ArmazemDto> CriarArmazemAsync(GuardarArmazem dados)
    {
        var nome = dados.Nome.Trim();
        if (await db.Armazens.AnyAsync(a => a.Nome == nome))
            throw new ConflitoException("Já existe um armazém com este nome.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var armazem = new Armazem { Nome = nome };
        db.Armazens.Add(armazem);
        await db.SaveChangesAsync();
        auditoria.Registar("armazem.criar", "Armazem", armazem.Id, new { nome });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return new ArmazemDto(armazem.Id, armazem.Nome, false, true);
    }

    // ---------- Artigos ----------

    public async Task<Pagina<ArtigoDto>> ListarArtigosAsync(FiltroArtigos f, CancellationToken ct)
    {
        var (pagina, tamanho) = Pagina<ArtigoDto>.Normalizar(f.Pagina, f.Tamanho);
        var q = db.Artigos.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(f.Pesquisa))
        {
            var p = f.Pesquisa.Trim();
            q = q.Where(a => a.Nome.Contains(p) || a.Codigo.Contains(p));
        }
        if (f.Tipo is { } tipo) q = q.Where(a => a.Tipo == tipo);
        if (f.Activo is bool activo) q = q.Where(a => a.Activo == activo);
        if (f.AbaixoMinimo) q = q.Where(a => a.StockMinimo > 0 && (db.StockSaldos.Where(s => s.ArtigoId == a.Id).Sum(s => (decimal?)s.Quantidade) ?? 0) <= a.StockMinimo);

        var total = await q.CountAsync(ct);
        var ids = await q.OrderBy(a => a.Nome).ThenBy(a => a.Id).Skip((pagina - 1) * tamanho).Take(tamanho).Select(a => a.Id).ToListAsync(ct);
        return new Pagina<ArtigoDto>(await ProjectarAsync(ids, ct), total, pagina, tamanho);
    }

    public async Task<ArtigoDto> ObterArtigoAsync(int id) =>
        (await ProjectarAsync([id], default)).SingleOrDefault() ?? throw new NaoEncontradoException("Artigo não encontrado.");

    public async Task<ArtigoDto> CriarArtigoAsync(GuardarArtigo dados)
    {
        if (dados.PermiteStockNegativo && !actual.TemPermissao(Permissoes.StockAjustar))
            throw new AcessoNegadoException("Autorizar stock negativo exige a permissão de ajustes de stock.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var artigo = new Artigo { Activo = true };
        await AplicarArtigo(artigo, dados);
        db.Artigos.Add(artigo);
        await db.SaveChangesAsync();
        auditoria.Registar("artigo.criar", "Artigo", artigo.Id, new { artigo.Codigo, artigo.Nome });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterArtigoAsync(artigo.Id);
    }

    public async Task<ArtigoDto> EditarArtigoAsync(int id, GuardarArtigo dados)
    {
        var artigo = await db.Artigos.FindAsync(id) ?? throw new NaoEncontradoException("Artigo não encontrado.");
        if (dados.PermiteStockNegativo != artigo.PermiteStockNegativo && !actual.TemPermissao(Permissoes.StockAjustar))
            throw new AcessoNegadoException("Alterar a autorização de stock negativo exige a permissão de ajustes de stock.");
        if (dados.UnidadeMedidaId != artigo.UnidadeMedidaId && await db.MovimentosStock.AnyAsync(m => m.ArtigoId == id))
            throw new RegraNegocioException("A unidade de um artigo com movimentos não pode ser alterada (os saldos ficariam incoerentes).");
        var negativoAntes = artigo.PermiteStockNegativo;
        await AplicarArtigo(artigo, dados);
        auditoria.Registar("artigo.editar", "Artigo", id, new { artigo.Codigo, artigo.Nome, artigo.StockMinimo,
            StockNegativo = negativoAntes == artigo.PermiteStockNegativo ? null : (bool?)artigo.PermiteStockNegativo });
        await db.SaveChangesAsync();
        return await ObterArtigoAsync(id);
    }

    public async Task DefinirArtigoActivoAsync(int id, bool activo)
    {
        var artigo = await db.Artigos.FindAsync(id) ?? throw new NaoEncontradoException("Artigo não encontrado.");
        if (!activo && await db.ReceitaItens.AnyAsync(ri => ri.ArtigoId == id && ri.Receita.Activa))
            throw new RegraNegocioException("O artigo é usado em fichas técnicas activas. Retire-o das fichas primeiro.");
        artigo.Activo = activo;
        auditoria.Registar(activo ? "artigo.activar" : "artigo.desactivar", "Artigo", id, new { artigo.Codigo });
        await db.SaveChangesAsync();
    }

    private async Task AplicarArtigo(Artigo artigo, GuardarArtigo dados)
    {
        var codigo = dados.Codigo.Trim().ToUpperInvariant();
        if (await db.Artigos.AnyAsync(a => a.Id != artigo.Id && a.Codigo == codigo))
            throw new ConflitoException($"Já existe um artigo com o código {codigo}.");
        if (!await db.UnidadesMedida.AnyAsync(u => u.Id == dados.UnidadeMedidaId && u.Activo))
            throw new RegraNegocioException("Unidade de medida inválida.");
        artigo.Codigo = codigo;
        artigo.Nome = dados.Nome.Trim();
        artigo.Tipo = dados.Tipo;
        artigo.UnidadeMedidaId = dados.UnidadeMedidaId;
        artigo.StockMinimo = decimal.Round(dados.StockMinimo, 3);
        artigo.PermiteStockNegativo = dados.PermiteStockNegativo;
    }

    private async Task<List<ArtigoDto>> ProjectarAsync(List<int> ids, CancellationToken ct)
    {
        var artigos = await db.Artigos.AsNoTracking().Include(a => a.UnidadeMedida).Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        var saldos = await db.StockSaldos.AsNoTracking().Where(s => ids.Contains(s.ArtigoId))
            .Select(s => new { s.ArtigoId, s.ArmazemId, Armazem = s.Armazem.Nome, s.Quantidade }).ToListAsync(ct);
        return [.. ids.Select(id => artigos.First(a => a.Id == id)).Select(a =>
        {
            var doArtigo = saldos.Where(s => s.ArtigoId == a.Id).ToList();
            var saldo = doArtigo.Sum(s => s.Quantidade);
            return new ArtigoDto(a.Id, a.Codigo, a.Nome, a.Tipo, a.UnidadeMedidaId, a.UnidadeMedida.Codigo, a.UnidadeMedida.Dimensao,
                a.StockMinimo, a.CustoMedio, a.UltimoCusto, a.PermiteStockNegativo, a.Activo, saldo,
                a.StockMinimo > 0 && saldo <= a.StockMinimo, decimal.Round(Math.Max(0, saldo) * a.CustoMedio, 2, MidpointRounding.AwayFromZero),
                [.. doArtigo.OrderBy(s => s.Armazem).Select(s => new SaldoArmazem(s.ArmazemId, s.Armazem, s.Quantidade))]);
        })];
    }

    // ---------- Movimentos ----------

    public async Task<Pagina<MovimentoStockDto>> ListarMovimentosAsync(FiltroMovimentos f, CancellationToken ct)
    {
        var (pagina, tamanho) = Pagina<MovimentoStockDto>.Normalizar(f.Pagina, f.Tamanho);
        var q = db.MovimentosStock.AsNoTracking();
        if (f.ArtigoId is int artigo) q = q.Where(m => m.ArtigoId == artigo);
        if (f.ArmazemId is int armazem) q = q.Where(m => m.ArmazemId == armazem);
        if (f.Tipo is { } tipo) q = q.Where(m => m.Tipo == tipo);
        if (f.DesdeUtc is DateTime desde) q = q.Where(m => m.DataUtc >= desde);
        if (f.AteUtc is DateTime ate) q = q.Where(m => m.DataUtc < ate);
        var total = await q.CountAsync(ct);
        var linhas = await q.OrderByDescending(m => m.DataUtc).ThenByDescending(m => m.Id).Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(m => new { m.Id, m.DataUtc, m.ArtigoId, Artigo = m.Artigo.Nome, Unidade = m.Artigo.UnidadeMedida.Codigo, Armazem = m.Armazem.Nome,
                m.Tipo, m.Quantidade, m.CustoUnitario, m.OrigemTipo, m.OrigemId, m.Motivo, m.UtilizadorId })
            .ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(linhas.Where(l => l.UtilizadorId is not null).Select(l => l.UtilizadorId!.Value), ct);
        return new Pagina<MovimentoStockDto>(
            [.. linhas.Select(l => new MovimentoStockDto(l.Id, l.DataUtc, l.ArtigoId, l.Artigo, l.Unidade, l.Armazem, l.Tipo, l.Quantidade, l.CustoUnitario,
                l.OrigemTipo, l.OrigemId, l.Motivo, l.UtilizadorId is int u ? nomes.GetValueOrDefault(u) : null))],
            total, pagina, tamanho);
    }

    /// <summary>Ajuste manual (com sinal) ou perda (sai do stock). Exige stock.ajustar e motivo.</summary>
    public async Task<ArtigoDto> AjustarAsync(AjustarStock dados)
    {
        if (dados.Tipo is not (TipoMovimentoStock.Ajuste or TipoMovimentoStock.Perda))
            throw new RegraNegocioException("Tipo de movimento inválido: use Ajuste ou Perda.");
        if (dados.Quantidade == 0 || (dados.Tipo == TipoMovimentoStock.Perda && dados.Quantidade < 0))
            throw new RegraNegocioException(dados.Tipo == TipoMovimentoStock.Perda ? "Indique a quantidade perdida (positiva)." : "A quantidade do ajuste não pode ser zero.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var artigo = await ArtigoActivoAsync(dados.ArtigoId);
        await ArmazemActivoAsync(dados.ArmazemId);
        var quantidade = dados.Tipo == TipoMovimentoStock.Perda ? -dados.Quantidade : dados.Quantidade;
        var movimento = await motor.MovimentarAsync(artigo, dados.ArmazemId, dados.Tipo, quantidade, artigo.CustoMedio, null, null, dados.Motivo.Trim());
        await db.SaveChangesAsync();
        auditoria.Registar(dados.Tipo == TipoMovimentoStock.Perda ? "stock.perda" : "stock.ajuste", "Artigo", artigo.Id,
            new { artigo.Codigo, Quantidade = quantidade, Armazem = dados.ArmazemId, Motivo = dados.Motivo.Trim(), Movimento = movimento.Id });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterArtigoAsync(artigo.Id);
    }

    public async Task<ArtigoDto> TransferirAsync(TransferirStock dados)
    {
        if (dados.ArmazemOrigemId == dados.ArmazemDestinoId)
            throw new RegraNegocioException("Os armazéns de origem e destino têm de ser diferentes.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var artigo = await ArtigoActivoAsync(dados.ArtigoId);
        var origem = await ArmazemActivoAsync(dados.ArmazemOrigemId);
        var destino = await ArmazemActivoAsync(dados.ArmazemDestinoId);
        var motivo = string.IsNullOrWhiteSpace(dados.Motivo) ? $"{origem.Nome} → {destino.Nome}" : dados.Motivo.Trim();
        // A saída nunca pode deixar a origem negativa (não se transfere o que não existe).
        await motor.MovimentarAsync(artigo, origem.Id, TipoMovimentoStock.TransferenciaSaida, -dados.Quantidade, artigo.CustoMedio, null, null, motivo);
        var saldoOrigem = await motor.SaldoAsync(artigo.Id, origem.Id);
        if (saldoOrigem.Quantidade < 0)
            throw new RegraNegocioException($"Só existem {saldoOrigem.Quantidade + dados.Quantidade:0.###} em {origem.Nome}.");
        await motor.MovimentarAsync(artigo, destino.Id, TipoMovimentoStock.TransferenciaEntrada, dados.Quantidade, artigo.CustoMedio, null, null, motivo);
        auditoria.Registar("stock.transferencia", "Artigo", artigo.Id, new { artigo.Codigo, dados.Quantidade, De = origem.Nome, Para = destino.Nome });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterArtigoAsync(artigo.Id);
    }

    // ---------- Inventários ----------

    public async Task<IReadOnlyList<InventarioResumo>> ListarInventariosAsync(CancellationToken ct) =>
        await db.Inventarios.AsNoTracking().OrderByDescending(i => i.CriadoEmUtc).Take(100)
            .Select(i => new InventarioResumo(i.Id, i.Armazem.Nome, i.Estado, i.CriadoEmUtc, i.ConfirmadoEmUtc, i.Linhas.Count,
                i.Linhas.Count(l => l.QuantidadeContada != l.QuantidadeSistema)))
            .ToListAsync(ct);

    public async Task<InventarioDto> ObterInventarioAsync(int id)
    {
        var inv = await db.Inventarios.AsNoTracking().Include(i => i.Armazem)
            .Include(i => i.Linhas).ThenInclude(l => l.Artigo).ThenInclude(a => a.UnidadeMedida)
            .FirstOrDefaultAsync(i => i.Id == id) ?? throw new NaoEncontradoException("Inventário não encontrado.");
        var nomes = await utilizadores.NomesAsync(new[] { inv.CriadoPorId, inv.ConfirmadoPorId }.Where(x => x is not null).Select(x => x!.Value));
        return new InventarioDto(inv.Id, inv.ArmazemId, inv.Armazem.Nome, inv.Estado, inv.CriadoEmUtc,
            inv.CriadoPorId is int c ? nomes.GetValueOrDefault(c) : null, inv.ConfirmadoEmUtc,
            inv.ConfirmadoPorId is int cf ? nomes.GetValueOrDefault(cf) : null, inv.Observacoes,
            [.. inv.Linhas.OrderBy(l => l.Artigo.Nome).Select(l => new InventarioLinhaDto(l.Id, l.ArtigoId, l.Artigo.Codigo, l.Artigo.Nome,
                l.Artigo.UnidadeMedida.Codigo, l.QuantidadeSistema, l.QuantidadeContada, l.QuantidadeContada - l.QuantidadeSistema))],
            inv.RowVersion);
    }

    /// <summary>Abre um inventário com todos os artigos activos e o saldo actual do armazém (contagem inicial = saldo).</summary>
    public async Task<InventarioDto> CriarInventarioAsync(NovoInventario dados)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        await ArmazemActivoAsync(dados.ArmazemId);
        if (await db.Inventarios.AnyAsync(i => i.ArmazemId == dados.ArmazemId && i.Estado == EstadoInventario.Aberto))
            throw new ConflitoException("Já existe um inventário aberto neste armazém.");
        var saldos = await db.StockSaldos.AsNoTracking().Where(s => s.ArmazemId == dados.ArmazemId).ToDictionaryAsync(s => s.ArtigoId, s => s.Quantidade);
        var artigos = await db.Artigos.AsNoTracking().Where(a => a.Activo).Select(a => a.Id).ToListAsync();
        if (artigos.Count == 0)
            throw new RegraNegocioException("Não há artigos activos para inventariar.");
        var inventario = new Inventario
        {
            ArmazemId = dados.ArmazemId,
            Observacoes = string.IsNullOrWhiteSpace(dados.Observacoes) ? null : dados.Observacoes.Trim(),
            Linhas = [.. artigos.Select(id => new InventarioLinha
            {
                ArtigoId = id,
                QuantidadeSistema = saldos.GetValueOrDefault(id),
                QuantidadeContada = Math.Max(0, saldos.GetValueOrDefault(id)),
            })],
        };
        db.Inventarios.Add(inventario);
        await db.SaveChangesAsync();
        auditoria.Registar("inventario.criar", "Inventario", inventario.Id, new { dados.ArmazemId, Artigos = artigos.Count });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterInventarioAsync(inventario.Id);
    }

    public async Task<InventarioDto> GuardarContagemAsync(int id, GuardarContagem dados)
    {
        var inventario = await CarregarInventarioAbertoAsync(id, dados.RowVersion);
        foreach (var c in dados.Linhas)
        {
            var linha = inventario.Linhas.FirstOrDefault(l => l.Id == c.LinhaId) ?? throw new RegraNegocioException("Linha de inventário inválida.");
            linha.QuantidadeContada = decimal.Round(c.QuantidadeContada, 3, MidpointRounding.AwayFromZero);
        }
        inventario.Observacoes = string.IsNullOrWhiteSpace(dados.Observacoes) ? inventario.Observacoes : dados.Observacoes.Trim();
        await db.SaveChangesAsync();
        return await ObterInventarioAsync(id);
    }

    /// <summary>
    /// Confirma: o stock de cada artigo passa a ser a quantidade contada. A diferença é calculada contra o saldo
    /// no momento da confirmação (vendas feitas depois de abrir o inventário são tidas em conta).
    /// </summary>
    public async Task<InventarioDto> ConfirmarInventarioAsync(int id, byte[]? versao)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var inventario = await CarregarInventarioAbertoAsync(id, versao);
        var artigos = await db.Artigos.Where(a => inventario.Linhas.Select(l => l.ArtigoId).Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        var ajustados = 0;
        foreach (var linha in inventario.Linhas)
        {
            var saldo = await motor.SaldoAsync(linha.ArtigoId, inventario.ArmazemId);
            linha.QuantidadeSistema = saldo.Quantidade;
            var diferenca = linha.QuantidadeContada - saldo.Quantidade;
            if (diferenca == 0) continue;
            var artigo = artigos[linha.ArtigoId];
            await motor.MovimentarAsync(artigo, inventario.ArmazemId, TipoMovimentoStock.Inventario, diferenca, artigo.CustoMedio,
                "InventarioLinha", linha.Id, $"Inventário n.º {inventario.Id}", ignorarNegativo: true);
            ajustados++;
        }
        inventario.Estado = EstadoInventario.Confirmado;
        inventario.ConfirmadoEmUtc = DateTime.UtcNow;
        inventario.ConfirmadoPorId = actual.Id;
        auditoria.Registar("inventario.confirmar", "Inventario", id, new { inventario.ArmazemId, ArtigosAjustados = ajustados });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterInventarioAsync(id);
    }

    public async Task CancelarInventarioAsync(int id, byte[]? versao)
    {
        var inventario = await CarregarInventarioAbertoAsync(id, versao);
        inventario.Estado = EstadoInventario.Cancelado;
        auditoria.Registar("inventario.cancelar", "Inventario", id, null);
        await db.SaveChangesAsync();
    }

    // ---------- Auxiliares ----------

    private async Task<Inventario> CarregarInventarioAbertoAsync(int id, byte[]? versao)
    {
        var inventario = await db.Inventarios.Include(i => i.Linhas).FirstOrDefaultAsync(i => i.Id == id)
                         ?? throw new NaoEncontradoException("Inventário não encontrado.");
        db.VerificarVersao(inventario, versao);
        if (inventario.Estado != EstadoInventario.Aberto)
            throw new RegraNegocioException("O inventário já não está aberto.");
        inventario.AlteradoEmUtc = DateTime.UtcNow;
        return inventario;
    }

    private async Task<Artigo> ArtigoActivoAsync(int id) =>
        await db.Artigos.FirstOrDefaultAsync(a => a.Id == id && a.Activo) ?? throw new RegraNegocioException("Artigo inexistente ou inactivo.");

    private async Task<Armazem> ArmazemActivoAsync(int id) =>
        await db.Armazens.FirstOrDefaultAsync(a => a.Id == id && a.Activo) ?? throw new RegraNegocioException("Armazém inexistente ou inactivo.");
}
