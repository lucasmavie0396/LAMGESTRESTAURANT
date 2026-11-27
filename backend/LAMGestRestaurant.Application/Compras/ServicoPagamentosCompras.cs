using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Compras;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Compras;

public record PagamentoCompraDto(int Id, int CompraId, DateTime DataUtc, decimal Valor, string Metodo, string? Referencia, int? SessaoCaixaId,
    string? Utilizador, EstadoPagamentoCompra Estado, DateTime? AnuladoEmUtc, string? MotivoAnulacao);

/// <summary>Com <see cref="SessaoCaixaId"/>, o dinheiro sai desse caixa (tem de estar aberto e ter o valor); sem ele, só fica registado.</summary>
public record NovoPagamentoCompra(
    [Range(typeof(decimal), "0.01", "100000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "O valor vai de 0,01 a 100 000 000.")] decimal Valor,
    [Range(1, int.MaxValue, ErrorMessage = "Escolha como foi pago.")] int MetodoPagamentoId,
    DateOnly? Data,
    [StringLength(100, ErrorMessage = "A referência não pode exceder 100 caracteres.")] string? Referencia,
    int? SessaoCaixaId);

public record AnularPagamentoCompra(
    [Required(ErrorMessage = "Indique o motivo.")][StringLength(500, MinimumLength = 3, ErrorMessage = "O motivo deve ter entre 3 e 500 caracteres.")] string Motivo);

/// <summary>
/// Pagamentos aos fornecedores das compras confirmadas (contas a pagar). Nunca acima do que falta pagar; pago pelo caixa,
/// sai da gaveta da sessão aberta; anulações com motivo (o dinheiro volta se a sessão ainda estiver aberta). Tudo na auditoria.
/// </summary>
public class ServicoPagamentosCompras(IAppDbContext db, IUtilizadorActual actual, IAuditoria auditoria, IConsultaUtilizadores utilizadores,
    ServicoCaixa caixa, ConfiguracoesSistema config, TimeProvider relogio)
{
    public async Task<IReadOnlyList<PagamentoCompraDto>> ListarAsync(int compraId, CancellationToken ct)
    {
        var linhas = await db.PagamentosCompras.AsNoTracking().Where(p => p.CompraId == compraId).OrderBy(p => p.DataUtc).ThenBy(p => p.Id)
            .Select(p => new { p, Metodo = p.MetodoPagamento.Nome }).ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(linhas.Select(l => l.p.UtilizadorId).Distinct(), ct);
        return [.. linhas.Select(l => Dto(l.p, l.Metodo, nomes.GetValueOrDefault(l.p.UtilizadorId)))];
    }

    public async Task<(PagamentoCompraDto Pagamento, bool Criado)> RegistarAsync(int compraId, NovoPagamentoCompra dados, string? chaveIdempotencia)
    {
        var chave = ServicoPedidos.ValidarChave(chaveIdempotencia);
        if (await db.PagamentosCompras.AsNoTracking().Where(p => p.IdempotencyKey == chave).Select(p => new { p.Id, p.CompraId }).FirstOrDefaultAsync() is { } existente)
            return existente.CompraId == compraId ? (await ObterAsync(existente.Id), false) : throw new ConflitoException("Esta chave já foi usada noutra compra.");
        var hoje = await config.HojeAsync(relogio);
        if (dados.Data > hoje)
            throw new RegraNegocioException("A data do pagamento não pode ser futura.");
        var agora = relogio.GetUtcNow().UtcDateTime;

        await using var transacao = await db.Database.BeginTransactionAsync();
        var compra = await db.Compras.Include(c => c.Fornecedor).FirstOrDefaultAsync(c => c.Id == compraId) ?? throw new NaoEncontradoException("Compra não encontrada.");
        if (compra.Estado != EstadoCompra.Confirmada)
            throw new RegraNegocioException("Só se pagam compras confirmadas.");
        var metodo = await db.MetodosPagamento.FirstOrDefaultAsync(m => m.Id == dados.MetodoPagamentoId && m.Activo)
                     ?? throw new RegraNegocioException("Método de pagamento inexistente ou inactivo.");
        if (metodo.Codigo is Facturacao.ServicoDocumentos.CodigoFacturaCredito or Cartoes.ServicoCartoes.CodigoMetodo)
            throw new RegraNegocioException($"Um fornecedor não se paga com {metodo.Nome}.");
        var referencia = string.IsNullOrWhiteSpace(dados.Referencia) ? null : dados.Referencia.Trim();
        if (metodo.ExigeReferencia && referencia is null)
            throw new RegraNegocioException($"Indique a referência da transacção {metodo.Nome}.");
        var valor = decimal.Round(dados.Valor, 2, MidpointRounding.AwayFromZero);
        var pago = await db.PagamentosCompras.Where(p => p.CompraId == compraId && p.Estado == EstadoPagamentoCompra.Valido).SumAsync(p => (decimal?)p.Valor) ?? 0m;
        var emDivida = compra.Total - pago;
        if (valor > emDivida)
            throw new RegraNegocioException(emDivida <= 0 ? "Esta compra já está paga." : $"O valor excede o que falta pagar ({emDivida:N2}).");

        SessaoCaixa? sessao = null;
        if (dados.SessaoCaixaId is int sessaoId)
        {
            sessao = await db.SessoesCaixa.FirstOrDefaultAsync(s => s.Id == sessaoId) ?? throw new RegraNegocioException("Sessão de caixa inexistente.");
            caixa.GarantirSessaoOperavel(sessao);
            if (metodo.ContaNaGaveta && valor > await caixa.EsperadoNoMetodoAsync(sessao, metodo.Id))
                throw new RegraNegocioException($"Não há {metodo.Nome} suficiente na gaveta para este pagamento.");
        }
        var pagamento = new PagamentoCompra
        {
            CompraId = compra.Id, Valor = valor, MetodoPagamentoId = metodo.Id, Referencia = referencia, SessaoCaixaId = sessao?.Id,
            UtilizadorId = actual.Id ?? throw new RegraNegocioException("Sessão de utilizador inválida."), IdempotencyKey = chave,
            // Pago pelo caixa: agora; caso contrário, meio-dia local da data indicada (ou de hoje).
            DataUtc = sessao is not null ? agora : (await config.IntervaloUtcAsync(dados.Data ?? hoje, dados.Data ?? hoje)).InicioUtc.AddHours(12),
        };
        db.PagamentosCompras.Add(pagamento);
        if (sessao is not null)
        {
            db.MovimentosCaixa.Add(new MovimentoCaixa
            {
                SessaoCaixaId = sessao.Id, Tipo = TipoMovimentoCaixa.Saida, MetodoPagamentoId = metodo.Id, Valor = valor,
                Motivo = $"Pagamento a {compra.Fornecedor.Nome} (compra {compra.NumeroDocumento})", UtilizadorId = pagamento.UtilizadorId, DataUtc = agora,
            });
            sessao.AlteradoEmUtc = agora;
        }
        await db.SaveChangesAsync();
        auditoria.Registar("compra.pagar", "PagamentoCompra", pagamento.Id,
            new { compra.NumeroDocumento, Fornecedor = compra.Fornecedor.Nome, valor, Metodo = metodo.Codigo, SessaoCaixa = sessao?.Id, EmDivida = emDivida - valor });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return (await ObterAsync(pagamento.Id), true);
    }

    public async Task<PagamentoCompraDto> AnularAsync(int id, AnularPagamentoCompra dados)
    {
        if (!actual.TemPermissao(Permissoes.ComprasAnular))
            throw new AcessoNegadoException("Não tem permissão para anular pagamentos a fornecedores.");
        var motivo = dados.Motivo.Trim();
        await using var transacao = await db.Database.BeginTransactionAsync();
        var p = await db.PagamentosCompras.Include(x => x.Compra).FirstOrDefaultAsync(x => x.Id == id) ?? throw new NaoEncontradoException("Pagamento não encontrado.");
        if (p.Estado == EstadoPagamentoCompra.Anulado)
            throw new RegraNegocioException("O pagamento já está anulado.");
        var agora = relogio.GetUtcNow().UtcDateTime;
        p.Estado = EstadoPagamentoCompra.Anulado;
        p.AnuladoEmUtc = agora;
        p.AnuladoPorId = actual.Id;
        p.MotivoAnulacao = motivo;
        var devolvido = false;
        if (p.SessaoCaixaId is int s && await db.SessoesCaixa.FirstOrDefaultAsync(x => x.Id == s && x.Estado == EstadoSessaoCaixa.Aberta) is { } sessao)
        {
            db.MovimentosCaixa.Add(new MovimentoCaixa
            {
                SessaoCaixaId = sessao.Id, Tipo = TipoMovimentoCaixa.Entrada, MetodoPagamentoId = p.MetodoPagamentoId, Valor = p.Valor,
                Motivo = $"Anulação do pagamento da compra {p.Compra.NumeroDocumento}: {motivo}", UtilizadorId = actual.Id!.Value, DataUtc = agora,
            });
            sessao.AlteradoEmUtc = agora;
            devolvido = true;
        }
        auditoria.Registar("compra.anular-pagamento", "PagamentoCompra", id, new { p.Compra.NumeroDocumento, p.Valor, Motivo = motivo, DevolvidoAoCaixa = devolvido });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(id);
    }

    private async Task<PagamentoCompraDto> ObterAsync(int id)
    {
        var l = await db.PagamentosCompras.AsNoTracking().Where(p => p.Id == id).Select(p => new { p, Metodo = p.MetodoPagamento.Nome }).FirstAsync();
        var nomes = await utilizadores.NomesAsync([l.p.UtilizadorId]);
        return Dto(l.p, l.Metodo, nomes.GetValueOrDefault(l.p.UtilizadorId));
    }

    private static PagamentoCompraDto Dto(PagamentoCompra p, string metodo, string? utilizador) =>
        new(p.Id, p.CompraId, p.DataUtc, p.Valor, metodo, p.Referencia, p.SessaoCaixaId, utilizador, p.Estado, p.AnuladoEmUtc, p.MotivoAnulacao);
}
