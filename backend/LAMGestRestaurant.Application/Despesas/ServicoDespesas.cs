using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Pessoal;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Despesas;

public record CategoriaDespesaDto(int Id, string Nome, bool Activo);

public record GuardarCategoriaDespesa([Required(ErrorMessage = "Indique o nome.")][StringLength(200)] string Nome);

public record DespesaDto(
    int Id,
    int CategoriaDespesaId,
    string Categoria,
    DateTime DataUtc,
    decimal Valor,
    string Descricao,
    string MetodoPagamento,
    string? Referencia,
    int? SessaoCaixaId,
    EstadoDespesa Estado,
    string? Utilizador,
    DateTime? AnuladaEmUtc,
    string? MotivoAnulacao,
    byte[] RowVersion);

/// <summary>Se SessaoCaixaId for indicado, a despesa é paga com dinheiro dessa sessão (movimento «Despesa» no caixa).</summary>
public record NovaDespesa(
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione a categoria.")] int CategoriaDespesaId,
    DateOnly Data,
    [Range(typeof(decimal), "0.01", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "O valor tem de ser positivo.")] decimal Valor,
    [Required(ErrorMessage = "Indique a descrição.")][StringLength(500, MinimumLength = 3, ErrorMessage = "A descrição deve ter entre 3 e 500 caracteres.")] string Descricao,
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o método de pagamento.")] int MetodoPagamentoId,
    [StringLength(100)] string? Referencia,
    int? SessaoCaixaId);

public record ResumoDespesas(DateOnly Desde, DateOnly Ate, decimal Total, int Numero, IReadOnlyList<LinhaValor> PorCategoria);

/// <summary>
/// Despesas. Confirmadas no registo; não se editam: corrige-se anulando (com permissão e motivo) e registando de novo.
/// Paga pela caixa: cria um movimento «Despesa» na sessão aberta do operador (não pode exceder o dinheiro na gaveta).
/// Ao anular, se a sessão ainda estiver aberta, o valor volta à caixa com uma entrada; se já fechou, o fecho não é alterado.
/// </summary>
public class ServicoDespesas(
    IAppDbContext db,
    IUtilizadorActual actual,
    IAuditoria auditoria,
    IConsultaUtilizadores utilizadores,
    ServicoCaixa caixa,
    ConfiguracoesSistema config,
    TimeProvider relogio)
{
    public async Task<IReadOnlyList<CategoriaDespesaDto>> CategoriasAsync(CancellationToken ct) =>
        await db.CategoriasDespesa.AsNoTracking().OrderBy(c => c.Nome).Select(c => new CategoriaDespesaDto(c.Id, c.Nome, c.Activo)).ToListAsync(ct);

    public async Task<CategoriaDespesaDto> GuardarCategoriaAsync(int? id, GuardarCategoriaDespesa dados)
    {
        var nome = dados.Nome.Trim();
        if (await db.CategoriasDespesa.AnyAsync(c => c.Id != id && c.Nome == nome))
            throw new ConflitoException("Já existe uma categoria com este nome.");
        var categoria = id is int existente
            ? await db.CategoriasDespesa.FindAsync(existente) ?? throw new NaoEncontradoException("Categoria não encontrada.")
            : db.CategoriasDespesa.Add(new CategoriaDespesa()).Entity;
        categoria.Nome = nome;
        await db.SaveChangesAsync();
        auditoria.Registar(id is null ? "despesa-categoria.criar" : "despesa-categoria.editar", "CategoriaDespesa", categoria.Id, new { nome });
        await db.SaveChangesAsync();
        return new CategoriaDespesaDto(categoria.Id, categoria.Nome, categoria.Activo);
    }

    public async Task DefinirCategoriaActivaAsync(int id, bool activa)
    {
        var c = await db.CategoriasDespesa.FindAsync(id) ?? throw new NaoEncontradoException("Categoria não encontrada.");
        c.Activo = activa;
        auditoria.Registar(activa ? "despesa-categoria.activar" : "despesa-categoria.desactivar", "CategoriaDespesa", id, null);
        await db.SaveChangesAsync();
    }

    public async Task<Pagina<DespesaDto>> ListarAsync(DateOnly desde, DateOnly ate, int? categoriaId, EstadoDespesa? estado, int pagina, int tamanho, CancellationToken ct)
    {
        (pagina, tamanho) = Pagina<DespesaDto>.Normalizar(pagina, tamanho);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var q = db.Despesas.AsNoTracking().Where(d => d.DataUtc >= inicio && d.DataUtc < fim);
        if (categoriaId is int c) q = q.Where(d => d.CategoriaDespesaId == c);
        if (estado is { } e) q = q.Where(d => d.Estado == e);
        var total = await q.CountAsync(ct);
        var linhas = await q.OrderByDescending(d => d.DataUtc).ThenByDescending(d => d.Id).Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(d => new { d, Categoria = d.CategoriaDespesa.Nome, Metodo = d.MetodoPagamento.Nome }).ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(linhas.Where(l => l.d.CriadoPorId is not null).Select(l => l.d.CriadoPorId!.Value), ct);
        return new Pagina<DespesaDto>([.. linhas.Select(l => ParaDto(l.d, l.Categoria, l.Metodo, nomes))], total, pagina, tamanho);
    }

    public async Task<DespesaDto> RegistarAsync(NovaDespesa dados)
    {
        if (dados.Data > await config.HojeAsync(relogio))
            throw new RegraNegocioException("A data da despesa não pode ser futura.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        if (!await db.CategoriasDespesa.AnyAsync(c => c.Id == dados.CategoriaDespesaId && c.Activo))
            throw new RegraNegocioException("Categoria inexistente ou inactiva.");
        var metodo = await db.MetodosPagamento.FirstOrDefaultAsync(m => m.Id == dados.MetodoPagamentoId && m.Activo)
                     ?? throw new RegraNegocioException("Método de pagamento inexistente ou inactivo.");
        if (metodo.Codigo == Facturacao.ServicoDocumentos.CodigoFacturaCredito)
            throw new RegraNegocioException("Uma despesa não se paga com Fatura a Crédito.");
        var valor = decimal.Round(dados.Valor, 2, MidpointRounding.AwayFromZero);
        var agora = relogio.GetUtcNow().UtcDateTime;

        SessaoCaixa? sessao = null;
        if (dados.SessaoCaixaId is int sessaoId)
        {
            sessao = await db.SessoesCaixa.FirstOrDefaultAsync(s => s.Id == sessaoId) ?? throw new RegraNegocioException("Sessão de caixa inexistente.");
            caixa.GarantirSessaoOperavel(sessao);
            if (metodo.ContaNaGaveta && valor > await caixa.EsperadoNoMetodoAsync(sessao, metodo.Id))
                throw new RegraNegocioException($"Não há {metodo.Nome} suficiente na gaveta para pagar esta despesa.");
        }

        // Paga pela caixa: data/hora actuais; caso contrário, meio-dia local da data indicada.
        var dataUtc = sessao is not null ? agora : (await config.IntervaloUtcAsync(dados.Data, dados.Data)).InicioUtc.AddHours(12);
        var despesa = new Despesa
        {
            CategoriaDespesaId = dados.CategoriaDespesaId,
            DataUtc = dataUtc,
            Valor = valor,
            Descricao = dados.Descricao.Trim(),
            MetodoPagamentoId = metodo.Id,
            Referencia = string.IsNullOrWhiteSpace(dados.Referencia) ? null : dados.Referencia.Trim(),
            SessaoCaixaId = sessao?.Id,
        };
        db.Despesas.Add(despesa);
        await db.SaveChangesAsync();
        if (sessao is not null)
        {
            db.MovimentosCaixa.Add(new MovimentoCaixa
            {
                SessaoCaixaId = sessao.Id, Tipo = TipoMovimentoCaixa.Despesa, MetodoPagamentoId = metodo.Id, Valor = valor,
                Motivo = $"Despesa: {despesa.Descricao}", DespesaId = despesa.Id, UtilizadorId = actual.Id!.Value, DataUtc = agora,
            });
            sessao.AlteradoEmUtc = agora;
        }
        auditoria.Registar("despesa.registar", "Despesa", despesa.Id, new { despesa.Valor, despesa.CategoriaDespesaId, Metodo = metodo.Codigo, SessaoCaixa = sessao?.Id });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(despesa.Id);
    }

    public async Task<DespesaDto> AnularAsync(int id, string motivo, byte[]? versao)
    {
        if (!actual.TemPermissao(Permissoes.DespesasAnular))
            throw new AcessoNegadoException("Não tem permissão para anular despesas.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var despesa = await db.Despesas.Include(d => d.SessaoCaixa).FirstOrDefaultAsync(d => d.Id == id) ?? throw new NaoEncontradoException("Despesa não encontrada.");
        db.VerificarVersao(despesa, versao);
        if (despesa.Estado == EstadoDespesa.Anulada)
            throw new RegraNegocioException("A despesa já está anulada.");
        var agora = relogio.GetUtcNow().UtcDateTime;
        despesa.Estado = EstadoDespesa.Anulada;
        despesa.AnuladaEmUtc = agora;
        despesa.AnuladaPorId = actual.Id;
        despesa.MotivoAnulacao = motivo.Trim();
        var devolvidaACaixa = false;
        if (despesa.SessaoCaixa is { Estado: EstadoSessaoCaixa.Aberta } sessao)
        {
            db.MovimentosCaixa.Add(new MovimentoCaixa
            {
                SessaoCaixaId = sessao.Id, Tipo = TipoMovimentoCaixa.Entrada, MetodoPagamentoId = despesa.MetodoPagamentoId, Valor = despesa.Valor,
                Motivo = $"Anulação da despesa n.º {despesa.Id}: {motivo.Trim()}", UtilizadorId = actual.Id!.Value, DataUtc = agora,
            });
            sessao.AlteradoEmUtc = agora;
            devolvidaACaixa = true;
        }
        auditoria.Registar("despesa.anular", "Despesa", id, new { despesa.Valor, Motivo = motivo.Trim(), DevolvidaACaixa = devolvidaACaixa });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(id);
    }

    public async Task<ResumoDespesas> ResumoAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        if (ate < desde) throw new RegraNegocioException("A data final é anterior à inicial.");
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var linhas = await db.Despesas.AsNoTracking().Where(d => d.Estado == EstadoDespesa.Confirmada && d.DataUtc >= inicio && d.DataUtc < fim)
            .GroupBy(d => d.CategoriaDespesa.Nome).Select(g => new LinhaValor(g.Key, g.Sum(d => d.Valor), g.Count())).ToListAsync(ct);
        return new ResumoDespesas(desde, ate, linhas.Sum(l => l.Valor), linhas.Sum(l => l.Quantidade), [.. linhas.OrderByDescending(l => l.Valor)]);
    }

    private async Task<DespesaDto> ObterAsync(int id)
    {
        var l = await db.Despesas.AsNoTracking().Where(d => d.Id == id)
            .Select(d => new { d, Categoria = d.CategoriaDespesa.Nome, Metodo = d.MetodoPagamento.Nome }).FirstAsync();
        var nomes = await utilizadores.NomesAsync(l.d.CriadoPorId is int u ? [u] : Array.Empty<int>());
        return ParaDto(l.d, l.Categoria, l.Metodo, nomes);
    }

    private static DespesaDto ParaDto(Despesa d, string categoria, string metodo, IReadOnlyDictionary<int, string> nomes) =>
        new(d.Id, d.CategoriaDespesaId, categoria, d.DataUtc, d.Valor, d.Descricao, metodo, d.Referencia, d.SessaoCaixaId, d.Estado,
            d.CriadoPorId is int u ? nomes.GetValueOrDefault(u) : null, d.AnuladaEmUtc, d.MotivoAnulacao, d.RowVersion);
}
